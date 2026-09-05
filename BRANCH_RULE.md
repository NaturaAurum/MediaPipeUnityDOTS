# Branch & PR Rules

All changes land via pull request. Direct commits and pushes to `main` and
`develop` are prohibited.

## Branches

| Branch       | Purpose                                  |
|--------------|------------------------------------------|
| `main`       | Release. Only release PRs merge here.    |
| `develop`    | Development integration. Default target. |
| `feature/*`  | New features.                            |
| `fix/*`      | Bug fixes.                               |
| `refactor/*` | Refactoring without behavior change.     |
| `chore/*`    | Docs, config, maintenance, chores.       |

## Pull Requests

- Base branch is `develop` by default. Only release PRs target `main`.
- One PR per change. Keep it small and reviewable.
- Merge with squash only (`gh pr merge --squash`). One PR adds exactly one
  commit to the target branch.
- Title prefix by category:

| Branch      | Prefix      | Example                        |
|-------------|-------------|--------------------------------|
| `feature/*` | `feat:`     | `feat: add pose smoothing`     |
| `fix/*`     | `fix:`      | `fix: correct UV flip`         |
| `refactor/*`| `refactor:` | `refactor: unify render loop`  |
| `chore/*`   | `chore:`    | `chore: update dependencies`   |
| docs-only   | `docs:`     | `docs: update BRANCH_RULE`     |

## Verify

- `git branch --show-current` is never `main` or `develop` when committing.
- `gh pr view --json baseRefName` shows `develop` (or `main` for releases).
- Merged PR adds exactly one commit to the target branch.
