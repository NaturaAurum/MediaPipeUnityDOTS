#!/usr/bin/env python3
"""Unity용 macOS arm64 브리지와 전이 의존성을 재배치해 묶는다.

빌드 호스트는 Homebrew를 사용할 수 있지만 배포물에는 그 로드 경로를 남기지 않는다.
Homebrew prefix 전체가 아니라 실제 Mach-O 의존 그래프만 따라간다.
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

MAX_BINARY_BYTES = 100 * 1024 * 1024
SYSTEM_PREFIXES = (
    "/usr/lib/",
    "/System/Library/",
    "/System/Volumes/Preboot/",
    "/Library/Apple/System/Library/",
)
FORMULA_ROOTS = ("/opt/homebrew", "/usr/local")
LICENSE_PREFIXES = ("license", "licence", "copying", "notice", "copyright", "patent")
LICENSE_SUFFIXES_TO_SKIP = (".html", ".htm", ".3ssl", ".1", ".pod")
COMPILED_INPUT_FILES = (
    "Build/.bazelrc",
    "Build/BuildMacosEditor.sh",
    "Build/SyncBridgeIntoWorkspace.sh",
)


class PackagingError(RuntimeError):
    pass


def run(*args: str, cwd: Path | None = None, capture: bool = False) -> str:
    result = subprocess.run(
        args,
        cwd=cwd,
        check=False,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
    )
    if result.returncode:
        detail = (result.stderr or "").strip() if capture else ""
        raise PackagingError(f"명령 실패 ({result.returncode}): {' '.join(args)}\n{detail}")
    return result.stdout if capture else ""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()

def build_input_digest(upstream_root: Path) -> str:
    native_root = Path(__file__).resolve().parent.parent
    records: list[str] = []
    for directory in ("Bridge", "Patches"):
        for source in sorted((native_root / directory).rglob("*")):
            if source.is_file() and "__pycache__" not in source.parts:
                records.append(f"{source.relative_to(native_root)} sha256={sha256_file(source)}")
    for relative in COMPILED_INPUT_FILES:
        source = native_root / relative
        records.append(f"{relative} sha256={sha256_file(source)}")
    records.append(f"mediapipe-commit={run('git', 'rev-parse', 'HEAD', cwd=upstream_root, capture=True).strip()}")
    return hashlib.sha256(("\n".join(records) + "\n").encode()).hexdigest()


def build_input_record(artifact: Path, upstream_root: Path) -> str:
    return (
        f"artifact-sha256={sha256_file(artifact)}\n"
        f"build-input-set-sha256={build_input_digest(upstream_root)}\n"
    )


def build_input_record_path(artifact: Path) -> Path:
    return artifact.with_name(artifact.name + ".build-inputs")


def write_build_input_record(artifact: Path, upstream_root: Path) -> None:
    if not artifact.is_file():
        raise PackagingError(f"빌드 산출물이 없습니다: {artifact}")
    build_input_record_path(artifact).write_text(build_input_record(artifact, upstream_root), encoding="utf-8")


def verify_build_input_record(artifact: Path, upstream_root: Path) -> None:
    record = build_input_record_path(artifact)
    if not record.is_file():
        raise PackagingError(f"빌드 입력 기록이 없습니다: {record}\n먼저 BuildMacosEditor.sh를 실행하세요.")
    expected = build_input_record(artifact, upstream_root)
    if record.read_text(encoding="utf-8") != expected:
        raise PackagingError("네이티브 빌드 입력이 산출물 생성 후 변경되었습니다. BuildMacosEditor.sh를 다시 실행하세요.")


def macho_dependencies(path: Path) -> list[str]:
    output = run("otool", "-L", str(path), capture=True)
    dependencies: list[str] = []
    for line in output.splitlines()[1:]:
        stripped = line.strip()
        if not stripped or " (" not in stripped:
            continue
        dependencies.append(stripped.split(" (", 1)[0])
    return dependencies


def macho_rpaths(path: Path) -> list[str]:
    output = run("otool", "-l", str(path), capture=True)
    return re.findall(r"^\s+path (.+?) \(offset \d+\)$", output, re.MULTILINE)


def is_system_dependency(value: str) -> bool:
    return value.startswith(SYSTEM_PREFIXES)


def dependency_basename(value: str) -> str:
    return value.rstrip("/").rsplit("/", 1)[-1]


def expand_loader_path(value: str, source: Path, root: Path) -> Path:
    value = value.replace("@loader_path", str(source.parent))
    value = value.replace("@executable_path", str(root.parent))
    return Path(value)


def resolve_dependency(
    value: str,
    source: Path,
    root: Path,
    known_by_name: dict[str, Path],
) -> Path | None:
    if is_system_dependency(value):
        return None

    name = dependency_basename(value)
    if value.startswith("@loader_path") or value.startswith("@executable_path"):
        candidate = expand_loader_path(value, source, root)
        if candidate.is_file():
            return candidate.resolve()
        return known_by_name.get(name)

    if value.startswith("@rpath/"):
        known = known_by_name.get(name)
        if known is not None:
            return known
        for rpath in macho_rpaths(source):
            candidate = expand_loader_path(rpath, source, root) / name
            if candidate.is_file():
                return candidate.resolve()
        return None

    candidate = Path(value)
    if candidate.is_file():
        return candidate.resolve()
    return None


def ensure_arm64(path: Path) -> None:
    if path.stat().st_size >= MAX_BINARY_BYTES:
        raise PackagingError(
            f"{path.name}가 100 MiB 이상입니다 ({path.stat().st_size} bytes). "
            "Git 바이너리 대신 릴리스 첨부/별도 바이너리 저장소를 사용해야 합니다."
        )
    description = run("file", str(path), capture=True)
    if "arm64" not in description:
        raise PackagingError(f"arm64 Mach-O가 아닙니다: {description.strip()}")


def formula_prefix(source: Path) -> tuple[str, Path] | None:
    resolved = source.resolve()
    parts = resolved.parts
    for root in FORMULA_ROOTS:
        root_parts = Path(root).parts
        if parts[: len(root_parts)] != root_parts:
            continue
        offset = len(root_parts)
        if len(parts) > offset + 1 and parts[offset] == "opt":
            formula = parts[offset + 1]
            prefix = Path(root) / "opt" / formula
            if prefix.exists():
                return formula, prefix.resolve()
        if len(parts) > offset + 2 and parts[offset] == "Cellar":
            formula = parts[offset + 1]
            prefix = Path(root) / "opt" / formula
            if prefix.exists():
                return formula, prefix.resolve()
    return None


def license_files(prefix: Path) -> list[Path]:
    found: dict[Path, Path] = {}
    if not prefix.exists():
        return []
    for candidate in prefix.rglob("*"):
        if not candidate.is_file() or any(part.lower() in {"man", "manual", "html"} for part in candidate.parts):
            continue
        name = candidate.name.lower()
        in_license_directory = any(part.lower() in {"licenses", "licences"} for part in candidate.relative_to(prefix).parts[:-1])
        if (not name.startswith(LICENSE_PREFIXES) and not in_license_directory) or name.endswith(LICENSE_SUFFIXES_TO_SKIP):
            continue
        try:
            if candidate.stat().st_size > 20 * 1024 * 1024:
                continue
            found[candidate.resolve()] = candidate
        except OSError:
            continue
    return sorted(found)


def safe_license_name(formula: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", formula.lower()).strip("-") or "dependency"


def plugin_meta(guid: str) -> str:
    return f"""fileFormatVersion: 2
guid: {guid}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Android: 1
        Exclude Editor: 0
        Exclude Linux64: 1
        Exclude OSXUniversal: 0
        Exclude WebGL: 1
        Exclude Win: 1
        Exclude Win64: 1
        Exclude iOS: 1
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: ARM64
        DefaultValueInitialized: true
        OS: OSX
  - first:
      Standalone: Linux64
    second:
      enabled: 0
      settings:
        CPU: None
  - first:
      Standalone: OSXUniversal
    second:
      enabled: 1
      settings:
        CPU: ARM64
  - first:
      Standalone: Win
    second:
      enabled: 0
      settings:
        CPU: None
  - first:
      Standalone: Win64
    second:
      enabled: 0
      settings:
        CPU: None
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def collect_graph(root: Path) -> tuple[list[Path], dict[Path, str], dict[str, Path]]:
    root = root.resolve()
    ensure_arm64(root)
    source_to_name: dict[Path, str] = {root: root.name}
    known_by_name: dict[str, Path] = {root.name: root}
    queue = [root]
    files: list[Path] = []

    while queue:
        source = queue.pop(0)
        if source in files:
            continue
        ensure_arm64(source)
        files.append(source)
        for dependency in macho_dependencies(source):
            if is_system_dependency(dependency):
                continue
            resolved = resolve_dependency(dependency, source, root, known_by_name)
            if resolved is None:
                raise PackagingError(
                    f"의존성을 찾을 수 없습니다: {source.name} -> {dependency}\n"
                    "@rpath 의존성은 LC_RPATH 또는 같은 패키지 파일로 확인되어야 합니다."
                )
            ensure_arm64(resolved)
            name = resolved.name
            existing = known_by_name.get(name)
            if existing is not None and existing != resolved:
                if sha256_file(existing) != sha256_file(resolved):
                    raise PackagingError(
                        f"같은 파일명에 서로 다른 dylib가 매핑됩니다: {name}\n"
                        f"{existing}\n{resolved}"
                    )
                source_to_name[resolved] = name
                continue
            known_by_name[name] = resolved
            source_to_name[resolved] = name
            queue.append(resolved)

    return files, source_to_name, known_by_name


def clean_old_bundle(destination: Path, manifest: Path, root_name: str) -> None:
    old_names: set[str] = set()
    if manifest.is_file():
        for line in manifest.read_text(encoding="utf-8").splitlines():
            if line.startswith("bundle: "):
                old_names.add(line.removeprefix("bundle: "))
    for old_name in sorted(old_names):
        if old_name == root_name:
            continue
        for path in (destination / old_name, destination / f"{old_name}.meta"):
            if path.exists():
                path.unlink()
    unexpected = sorted(
        path.name
        for path in destination.glob("*.dylib")
        if path.name != root_name and path.name not in old_names
    )
    if unexpected:
        raise PackagingError(
            "기존 패키지에 관리되지 않는 dylib가 있습니다: "
            + ", ".join(unexpected)
            + " (삭제하지 않고 중단합니다)"
        )


def relink_stage(
    stage: Path,
    files: list[Path],
    source_to_name: dict[Path, str],
    known_by_name: dict[str, Path],
    root: Path,
) -> None:
    for source in sorted(files, key=lambda item: source_to_name[item]):
        destination = stage / source_to_name[source]
        run("install_name_tool", "-id", f"@loader_path/{destination.name}", str(destination))
        for dependency in macho_dependencies(source):
            if is_system_dependency(dependency):
                continue
            resolved = resolve_dependency(dependency, source, root, known_by_name)
            if resolved is None:
                raise PackagingError(f"재배치할 의존성을 찾을 수 없습니다: {source.name} -> {dependency}")
            if resolved == source:
                continue
            target_name = source_to_name.get(resolved)
            if target_name is None:
                target_name = known_by_name.get(resolved.name, resolved).name
            run(
                "install_name_tool",
                "-change",
                dependency,
                f"@loader_path/{target_name}",
                str(destination),
            )
        for rpath in macho_rpaths(source):
            if rpath.startswith("/") and not is_system_dependency(f"{rpath}/"):
                run("install_name_tool", "-delete_rpath", rpath, str(destination))

    # 코드 서명은 모든 의존성 수정이 끝난 뒤 수행하고, 브리지는 마지막에 서명합니다.
    ordered = sorted(files, key=lambda item: source_to_name[item])
    for source in ordered:
        if source_to_name[source] == "libmpud_bridge.dylib":
            continue
        run("codesign", "--force", "-s", "-", str(stage / source_to_name[source]))
    run("codesign", "--force", "-s", "-", str(stage / "libmpud_bridge.dylib"))

def copy_stage(
    stage: Path,
    destination: Path,
    files: list[Path],
    source_to_name: dict[Path, str],
    root_name: str,
) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    for source in sorted(files, key=lambda item: source_to_name[item]):
        target = destination / source_to_name[source]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(stage / source_to_name[source], target)
        target.chmod(0o755)
        if target.name != root_name:
            guid = hashlib.sha1(f"Runtime/Plugins/macOS/{target.name}".encode("utf-8")).hexdigest()[:32]
            target.with_name(f"{target.name}.meta").write_text(plugin_meta(guid), encoding="utf-8")


def validate_stage(stage: Path, files: list[Path], source_to_name: dict[Path, str]) -> None:
    for source in sorted(files, key=lambda item: source_to_name[item]):
        destination = stage / source_to_name[source]
        ensure_arm64(destination)
        for dependency in macho_dependencies(destination):
            if is_system_dependency(dependency):
                continue
            if not dependency.startswith("@loader_path/"):
                raise PackagingError(f"Homebrew/절대 경로가 남았습니다: {destination.name} -> {dependency}")
            target = stage / dependency.removeprefix("@loader_path/")
            if not target.is_file():
                raise PackagingError(f"패키지 내부 의존성 파일이 없습니다: {destination.name} -> {target.name}")
        for rpath in macho_rpaths(destination):
            if rpath.startswith("/") and not is_system_dependency(f"{rpath}/"):
                raise PackagingError(f"외부 RPATH가 남았습니다: {destination.name} -> {rpath}")
        run("codesign", "--verify", "--strict", str(destination))


def write_static_notices(license_dir: Path, upstream_root: Path) -> None:
    bazel = ("bazelisk", f"--bazelrc={Path(__file__).with_name('.bazelrc')}")
    repositories = run(
        *bazel, "cquery", "-c", "opt", "--repo_env=HERMETIC_PYTHON_VERSION=3.11",
        "deps(//mediapipe/mpud_bridge:libmpud_bridge.dylib)", "--output=starlark",
        '--starlark:expr=target.label.workspace_name if "CcInfo" in providers(target) else ""',
        cwd=upstream_root, capture=True,
    )
    external = Path(run(*bazel, "info", "output_base", cwd=upstream_root, capture=True).strip()) / "external"
    # 빌드 도구 자체와 별도로 수집한 동적 OpenCV를 제외한다. 헤더 전용 의존성도 포함한다.
    for repository in sorted(set(repositories.split()) - {"bazel_tools", "rules_cc~", "macos_opencv"}):
        source = (external / repository).resolve()
        candidates = license_files(source)
        if repository == "fft2d":
            candidates.append(source / "readme.txt")
        if not candidates:
            raise PackagingError(f"정적 의존성 고지를 찾을 수 없습니다: {repository}")
        with (license_dir / f"static-{safe_license_name(repository)}.txt").open("w", encoding="utf-8") as stream:
            stream.write(f"Bazel C/C++ source dependency: {repository}\n")
            stream.write("상위 저장소의 고지를 함께 보존하며, 모든 하위 모듈이 링크됐다는 뜻은 아닙니다.\n\n")
            for candidate in candidates:
                stream.write(f"===== {candidate.relative_to(source)} =====\n")
                stream.write(candidate.read_text(encoding="utf-8", errors="replace") + "\n\n")


def write_licenses(
    output_dir: Path,
    files: list[Path],
    upstream_root: Path,
) -> list[Path]:
    license_dir = output_dir / "ThirdPartyLicenses"
    if license_dir.exists():
        shutil.rmtree(license_dir)
    license_dir.mkdir(parents=True)

    prefixes: dict[str, Path] = {}
    for source in files:
        owner = formula_prefix(source)
        if owner is None:
            if source.name == "libmpud_bridge.dylib":
                continue
            raise PackagingError(f"라이선스 출처를 확인할 수 없습니다: {source}")
        formula, prefix = owner
        prefixes[formula] = prefix

    for formula, prefix in sorted(prefixes.items()):
        candidates = license_files(prefix)
        if not candidates:
            raise PackagingError(f"라이선스 파일을 찾을 수 없습니다: {formula}")
        target = license_dir / f"{safe_license_name(formula)}.txt"
        with target.open("w", encoding="utf-8") as stream:
            stream.write(f"Dependency: {formula}\nInstalled version: {prefix.name}\n\n")
            for candidate in candidates:
                relative = candidate.relative_to(prefix)
                stream.write(f"===== {relative} =====\n")
                stream.write(candidate.read_text(encoding="utf-8", errors="replace"))
                stream.write("\n\n")
            for recipe in sorted((prefix / ".brew").glob("*.rb")):
                stream.write(f"===== Installed source/build recipe: {recipe.name} =====\n")
                stream.write(f"sha256: {sha256_file(recipe)}\n")
                stream.write(recipe.read_text(encoding="utf-8") + "\n\n")

    mediapipe_license = upstream_root / "LICENSE"
    if not mediapipe_license.is_file():
        raise PackagingError(f"MediaPipe 라이선스를 찾을 수 없습니다: {mediapipe_license}")
    (license_dir / "mediapipe.txt").write_text(
        "Dependency: MediaPipe upstream\n\n" + mediapipe_license.read_text(encoding="utf-8"),
        encoding="utf-8",
    )

    write_static_notices(license_dir, upstream_root)
    shutil.copy2(Path(__file__).with_name("HomebrewRecipeLicense.txt"), license_dir / "homebrew-recipes.txt")

    combined = output_dir / "THIRD_PARTY_LICENSES.txt"
    with combined.open("w", encoding="utf-8") as stream:
        stream.write("MediaPipeUnityDots native dependency notices\n\n")
        for license_path in sorted(license_dir.glob("*.txt")):
            stream.write(f"\n\n######## {license_path.name} ########\n\n")
            stream.write(license_path.read_text(encoding="utf-8"))
    return sorted(license_dir.glob("*.txt"))


def write_manifest(
    destination: Path,
    files: list[Path],
    source_to_name: dict[Path, str],
    license_paths: list[Path],
    upstream_root: Path,
) -> None:
    manifest = destination / "native-dependency-manifest.txt"
    total = sum((destination / source_to_name[source]).stat().st_size for source in files)
    minimum_versions = []
    for source in files:
        commands = run("otool", "-l", str(destination / source_to_name[source]), capture=True)
        minimum_versions.extend(
            tuple(int(part) for part in value.split("."))
            for value in re.findall(r"\bminos\s+([0-9.]+)", commands)
        )
    lines = [
        "MediaPipeUnityDots macOS arm64 native bundle",
        "architecture: arm64",
        f"bundle-bytes: {total}",
        f"bundle-files: {len(files)}",
        "system-frameworks: provided by macOS (not bundled)",
        f"mediapipe-commit: {run('git', 'rev-parse', 'HEAD', cwd=upstream_root, capture=True).strip()}",
        f"bazel-version: {(upstream_root / '.bazelversion').read_text().strip()}",
        "minimum-macos: " + ".".join(map(str, max(minimum_versions))),
        f"build-input-set-sha256: {build_input_digest(upstream_root)}",
    ]
    for source in sorted(files, key=lambda item: source_to_name[item]):
        path = destination / source_to_name[source]
        owner = formula_prefix(source)
        lines.extend(
            [
                f"bundle: {path.name}",
                f"source: {owner[0] if owner else 'bridge'}",
                f"size: {path.stat().st_size}",
                f"sha256: {sha256_file(path)}",
            ]
        )
    native_root = Path(__file__).resolve().parent.parent
    for directory in ("Bridge", "Build", "Patches"):
        for source in sorted((native_root / directory).rglob("*")):
            if source.is_file() and "__pycache__" not in source.parts:
                lines.append(f"build-input: {source.relative_to(native_root)} sha256={sha256_file(source)}")
    lines.append("license-files:")
    lines.extend(f"  {path.relative_to(destination)}" for path in license_paths)
    manifest.write_text("\n".join(lines) + "\n", encoding="utf-8")




def package(root: Path, destination: Path, upstream_root: Path) -> None:
    if sys.platform != "darwin":
        raise PackagingError("macOS에서만 실행할 수 있습니다.")
    if not shutil.which("otool") or not shutil.which("install_name_tool") or not shutil.which("codesign"):
        raise PackagingError("otool, install_name_tool, codesign이 필요합니다.")
    if not root.is_file():
        raise PackagingError(f"빌드 산출물이 없습니다: {root}\n먼저 BuildMacosEditor.sh를 실행하세요.")
    verify_build_input_record(root, upstream_root)

    files, source_to_name, known_by_name = collect_graph(root)
    destination.mkdir(parents=True, exist_ok=True)
    manifest = destination / "native-dependency-manifest.txt"

    with tempfile.TemporaryDirectory(prefix="mpud-native-") as temporary:
        stage = Path(temporary)
        for source in files:
            shutil.copy2(source, stage / source_to_name[source])
            (stage / source_to_name[source]).chmod(0o755)
        relink_stage(stage, files, source_to_name, known_by_name, root.resolve())
        validate_stage(stage, files, source_to_name)
        write_licenses(stage, files, upstream_root)
        clean_old_bundle(destination, manifest, root.name)
        copy_stage(stage, destination, files, source_to_name, root.name)
        target_licenses = destination / "ThirdPartyLicenses"
        target_licenses.mkdir(exist_ok=True)
        staged_licenses = {path.name: path for path in (stage / "ThirdPartyLicenses").glob("*.txt")}
        for installed in target_licenses.glob("*.txt"):
            if installed.name not in staged_licenses:
                installed.unlink()
                installed.with_suffix(installed.suffix + ".meta").unlink(missing_ok=True)
        for name, source in staged_licenses.items():
            shutil.copy2(source, target_licenses / name)
        shutil.copy2(stage / "THIRD_PARTY_LICENSES.txt", destination / "THIRD_PARTY_LICENSES.txt")
        write_manifest(destination, files, source_to_name, sorted(target_licenses.glob("*.txt")), upstream_root)

    print(f"[Package] {len(files)} arm64 dylib(s), {sum((destination / source_to_name[item]).stat().st_size for item in files)} bytes")
    print(f"[Package] Unity plugin: {destination}")
    print(f"[Package] Dependency manifest: {manifest}")
    print("[Package] All non-system Mach-O dependencies use @loader_path and ad-hoc signatures")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", type=Path, required=True)
    parser.add_argument("--destination", type=Path)
    parser.add_argument("--upstream", type=Path, required=True)
    parser.add_argument("--record-build-inputs", action="store_true")
    args = parser.parse_args()
    try:
        if args.record_build_inputs:
            write_build_input_record(args.artifact, args.upstream)
            return 0
        if args.destination is None:
            parser.error("--destination is required unless --record-build-inputs is used")
        package(args.artifact, args.destination, args.upstream)
    except (OSError, PackagingError, subprocess.CalledProcessError) as error:
        print(f"[Error] {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
