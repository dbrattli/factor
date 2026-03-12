# Releasing

This project uses [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt)
for release automation and [Conventional Commits](https://www.conventionalcommits.org/)
for versioning.

## Commit conventions

PR titles must follow the conventional commit format (enforced by CI):

| Prefix | Version bump | Example |
| --- | --- | --- |
| `feat:` | minor | `feat: add schedule combinator` |
| `fix:` | patch | `fix: correct reply channel handling` |
| `feat!:` | major | `feat!: rename Actor to ActorOp` |
| `chore:` | patch | `chore: update dependencies` |
| `docs:` | patch | `docs: update README` |
| `refactor:` | patch | `refactor: simplify supervision` |

Other valid prefixes: `test`, `perf`, `ci`, `build`, `style`, `revert`.

## Creating a release

```bash
just shipit
```

This will:

1. Analyze commits since the last release
2. Determine the next semantic version
3. Update `CHANGELOG.md`
4. Create a GitHub release with the version tag (e.g. `v5.0.0-rc.2`)

The GitHub release triggers the publish workflow, which:

1. Packs the NuGet package (`Fable.Actor`)
2. Pushes it to nuget.org using the `NUGET_API_KEY` secret

## Prerequisites

- `NUGET_API_KEY` repository secret (glob pattern: `Fable.Actor*`)
- `GITHUB_TOKEN` or `gh` CLI authenticated (for ShipIt to create releases)
