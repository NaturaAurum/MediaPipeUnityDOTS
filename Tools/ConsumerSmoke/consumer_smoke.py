#!/usr/bin/env python3
"""빈 Unity 프로젝트에서 지정한 Git revision의 Editor·macOS Player 추론을 검증한다."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import urllib.request

PACKAGE = "com.natura-aurum.mediapipe-unity-dots"
FIXTURES = {
    "male_full_height_hands.jpg": "8a7fe5be8b90d6078b09913ca28f7e5d342f8d3cde856ab4e3327d2970b887f8",
    "portrait.jpg": "a6f11efaa834706db23f275b6115058fa87fc7f14362681e6abe14e82749de3e",
}
PLAYER_SANDBOX = """(version 1)
(allow default)
(deny network*)
(deny file-read* (subpath "/opt/homebrew") (subpath "/usr/local/Cellar") (subpath "/usr/local/opt"))
"""
RUNNER = "MediaPipeUnityDots.Sample.LandmarkApi.Editor.LandmarkApiSmokeRunner"
BOOTSTRAP = '''using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

public static class ImportSamples
{
    public static void Run()
    {
        try
        {
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name != "com.natura-aurum.mediapipe-unity-dots") continue;
                foreach (var sample in new[]
                {
                    new[] { "LandmarkApi", "Landmark API" },
                    new[] { "TrackingDemo", "Tracking Demo" },
                })
                {
                    var source = Path.Combine(package.resolvedPath, "Samples~", sample[0]);
                    var target = Path.Combine(Application.dataPath, "Samples", package.displayName, package.version, sample[1]);
                    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var destination = Path.Combine(target, file.Substring(source.Length + 1));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(file, destination, false);
                    }
                }
                Debug.Log("[MPUD CONSUMER SMOKE] GIT_SAMPLES_IMPORTED " + package.resolvedPath);
                EditorApplication.Exit(0);
                return;
            }
            throw new InvalidOperationException("Git package did not resolve");
        }
        catch (Exception exception)
        {
            Debug.LogError(exception);
            EditorApplication.Exit(1);
        }
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True, help="Unity 실행 파일")
    parser.add_argument("--git-url", required=True, help="UPM Git URL, ?path=...#revision 포함")
    parser.add_argument("--workdir", type=Path, help="새로 만들 디렉터리. 기존 경로는 덮어쓰지 않는다")
    parser.add_argument("--timeout", type=int, default=900, help="각 프로세스 제한 시간(초)")
    parser.add_argument("--editor-only", action="store_true", help="Player 검증을 명시적으로 제외")
    args = parser.parse_args()
    if not args.unity.is_file():
        parser.error("--unity 실행 파일이 없습니다")
    if "#" not in args.git_url or not args.git_url.rsplit("#", 1)[1]:
        parser.error("재현 가능한 검증을 위해 Git revision을 지정해야 합니다")
    if args.timeout <= 0:
        parser.error("--timeout은 양수여야 합니다")
    if args.workdir:
        work = args.workdir.resolve()
        work.mkdir(parents=True, exist_ok=False)
    else:
        work = Path(tempfile.mkdtemp(prefix="mpud-consumer-"))
    project = work / "Project"
    for directory in ("Assets/Editor", "Packages", "ProjectSettings"):
        (project / directory).mkdir(parents=True)
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
        PACKAGE: args.git_url,
    }}, indent=2) + "\n")
    bootstrap = project / "Assets/Editor/ImportLandmarkApi.cs"
    bootstrap.write_text(BOOTSTRAP)
    print(f"검증 산출물 / Verification artifacts: {work}", flush=True)
    environment = os.environ.copy()
    # 빌드 호스트의 loader override로 누락된 배포 라이브러리를 숨기지 않는다.
    for key in tuple(environment):
        if key.startswith("DYLD_"):
            del environment[key]

    def run(name, command, marker):
        log = work / f"{name}.log"
        with (work / f"{name}-process.log").open("w") as output:
            result = subprocess.run(command + ["-logFile", str(log)], cwd=work,
                                    env=environment, stdout=output, stderr=subprocess.STDOUT,
                                    timeout=args.timeout, check=False)
        text = log.read_text(errors="replace") if log.exists() else ""
        if result.returncode != 0 or marker not in text:
            raise RuntimeError(f"{name} failed (exit={result.returncode}); see {log}")
        print(f"PASS {name}: {log}", flush=True)

    def unity(name, method, marker, *extra):
        run(name, [str(args.unity.resolve()), "-batchmode", "-nographics",
                   "-projectPath", str(project), "-executeMethod", method, *extra], marker)

    unity("import", "ImportSamples.Run", "GIT_SAMPLES_IMPORTED")
    bootstrap.unlink()
    bootstrap.with_suffix(".cs.meta").unlink(missing_ok=True)
    lock = json.loads((project / "Packages/packages-lock.json").read_text())
    resolution = lock["dependencies"][PACKAGE]
    if resolution.get("source") != "git":
        raise RuntimeError(f"Git 설치가 아닙니다: {resolution}")
    (work / "git-resolution.json").write_text(json.dumps(resolution, indent=2) + "\n")
    # 코어와 두 선택 샘플을 가져온 첫 컴파일 이후, 이미지 스모크에 필요한 모듈을 추가한다.
    manifest_path = project / "Packages/manifest.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["dependencies"]["com.unity.modules.imageconversion"] = "1.0.0"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    fixture_dir = project / "Assets/StreamingAssets/MediaPipe/Fixtures"
    fixture_dir.mkdir(parents=True)
    for filename, digest in FIXTURES.items():
        url = "https://storage.googleapis.com/mediapipe-assets/" + filename
        with urllib.request.urlopen(url, timeout=60) as response:
            image = response.read()
        if hashlib.sha256(image).hexdigest() != digest:
            raise RuntimeError(f"공식 검증 이미지의 SHA-256이 다릅니다: {filename}")
        (fixture_dir / filename).write_bytes(image)
    unity("models", "MediaPipeUnityDots.EditorTool.DownloadDepthModel.PrepareBatch",
          "[MPUD] Batch model preparation complete", "-mpudModels", "Hand,Face,Pose,Holistic")
    unity("editor", RUNNER + ".Run", "[MPUD CONSUMER SMOKE] EDITOR_PASS")
    if not args.editor_only:
        player = work / "LandmarkApiSmoke.app"
        unity("build", RUNNER + ".BuildStandalone", "[MPUD CONSUMER SMOKE] PLAYER_BUILD_PASS",
              "-mpudPlayerPath", str(player))
        binaries = [path for path in (player / "Contents/MacOS").iterdir() if path.is_file()]
        if len(binaries) != 1:
            raise RuntimeError(f"Player 실행 파일을 결정하지 못했습니다: {binaries}")
        run("player", ["/usr/bin/sandbox-exec", "-p", PLAYER_SANDBOX,
                       str(binaries[0]), "-batchmode", "-nographics"],
            "[MPUD CONSUMER SMOKE] PLAYER_PASS")
    print("PASS Git consumer" + (" (Editor only)" if args.editor_only else " (Editor + macOS Player)"))


if __name__ == "__main__":
    main()
