# Contributing to Ranvier.Css

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) for commit messages and pull request titles:

```text
type(scope): summary
```

The scope is optional and must use lowercase letters, digits, `.`, `_`, `/` or `-`.
Supported contribution types are `build`, `bench`, `chore`, `ci`, `docs`, `feat`,
`fix`, `perf`, `refactor`, `revert`, `style` and `test`.

```text
feat(css): add logical properties
fix: handle an empty input
feat!: remove the obsolete API
```

Mark breaking changes with `!` before the colon, or a `BREAKING CHANGE:` footer.
Explain the migration in the commit body or pull request description.

The **Contribution / Conventional commits** check validates human pull request
titles; bot pull requests are exempt, matching the Ranvier workflow. It does not
lint every commit in the branch. Use squash merging and preserve the conventional
PR title as the squash commit subject. If maintainers allow other merge methods,
every commit that reaches the release branch must follow the convention too.

## Before opening a pull request

Use `dotnet fsi build.fsx -- dev setup --with-browser` to restore the pinned
tools/npm dependencies and install Chromium. Run `dotnet fsi build.fsx -- ci`
for the complete local equivalent of the GitHub CI gates. Individual checks use
`test --group dotnet`, `bindings`, `assets` or `browser`; bare `test` runs all four.

For changes spanning this repository and Ranvier, use
`dotnet fsi build.fsx -- dev local [--ranvier-root <path>]`. This clones a missing
sibling checkout and enables project references using ignored `Local.Build.props`.
Existing checkouts are preserved. `dev packages` returns to pinned package references.
Open both repositories in your IDE when editing them together; commit and submit
each repository's changes separately. CI uses packages unless a source-integration
run explicitly enables `UseLocalRanvier=true` with a pinned upstream checkout.

Use .NET 10, the .NET 8 runtime and Node.js 22 or later. Generated CSS bindings
are excluded from formatting: regenerate them with the generator and verify them
through `test --group bindings`.

Install the .NET SDK selected by `global.json` (the build CLI requires .NET 10 or
later). When targeting .NET 8 or 9, install that runtime too so the tests can run.
Restore the pinned local tools with `dotnet tool restore`.

Run `dotnet fsi build.fsx -- test`.
Use the build CLI's `format` command to apply Fantomas formatting, then
`format --dry-format` to check it. CI checks formatting without rewriting files.
Run the build CLI's `compile` command to check JavaScript compilation with fickl.

## EasyBuild release requirements

[EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt) calculates
release versions from conventional commits. Use `feat` for features and `fix`
for fixes; breaking changes must carry the marker above. Inspect the installed
tool's `shipit conventions` command for its supported release categories.

Each packaged project has a `CHANGELOG.md` next to its `.fsproj`. Its YAML
`updaters` entry points to exactly one unconditional literal `<Version>` in that
project. Keep the version in the project and the latest changelog heading in
sync. ShipIt owns release version changes; do not run a separate numeric bump or
hand-edit generated release entries.

Add a project to the solution and give it its own changelog/updater for independent
versioning. The build CLI's `shipit init project --changelog <path> --project <path>`
command can register this. Use one changelog with multiple XML updaters when
projects intentionally share a version.

The `bump` command calculates and applies a release locally; use `--dry-run` to
preview it and `--allow-branch master` when working on `master` instead of `main`.
It does not publish packages. `shipit github` creates a release PR by default.
Both need restored tools, a Git repository with full history, and valid
conventional commits. GitHub operations also need GitHub authentication.

After CI succeeds on a push to `main` or `master`, the ShipIt workflow opens or
updates a release PR. Merge it using its `chore: release ...` title. CI validates
and packs that release commit, then the publish workflow uploads those exact CI
artifacts to NuGet using trusted publishing. Keep the release title prefix: it
is how the publish workflow recognizes a release.

The build CLI's `publish --dry-publish` command builds, tests and packs locally
without uploading anything. Maintainers can also publish locally with
`NUGET_API_KEY` or `--nuget-key`; credentials are masked in command output.

### Maintainer setup

1. Enable squash merging and allow GitHub Actions to create and approve pull
   requests in repository Actions settings. Require the `CI result` and Contribution checks
   in branch protection.
2. For release PRs that trigger CI automatically, set `SHIPIT_TOKEN` to a fine-grained
   token with repository Contents and Pull requests write permissions. The workflow
   falls back to `GITHUB_TOKEN`; PRs created with that token require a manual CI run
   on the release branch because GitHub suppresses automatic workflow triggers.
3. Set the Actions variable `NUGET_USER` to your NuGet.org username. Create a NuGet
   trusted publishing policy for `ranvier-org/Ranvier.Css` and the workflow
   filename `publish.yml`. No long-lived NuGet key is needed in GitHub secrets.
4. Before the first release, ensure the initial commit is conventional (for example
   `feat: initialise the project`). For an existing package, seed its changelog with
   the actual latest release and set `last_commit_released` to that release's commit.

`shipit init github --dry-run` previews EasyBuild's suggested GitHub settings.
Applying them requires an authenticated `gh` CLI and repository administration
rights; this is an explicit maintainer operation.

### Imported release baseline

The CSS libraries preserve the versions already published from Ranvier. Before
enabling automated releases in this new repository, commit the imported baseline
and set `last_commit_released` in each changelog to that import commit. Do not
reset package versions or attempt to overwrite an existing NuGet version.

When a release requires a newer Ranvier.View, first test the exact upstream and
downstream revisions in local-reference mode. Publish View, wait until its exact
version restores from NuGet, update `RanvierViewVersion` here, then run all CSS
gates in package mode and publish CSS. Each repository keeps its own version and
publishing identity. Cross-repository automation should pass explicit commit SHAs
and versions, rather than build whichever branch tip is newest.
