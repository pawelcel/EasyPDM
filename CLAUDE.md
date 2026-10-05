# Working on EasyPDM

Conventions that are easy to miss and have each been got wrong at least once. Everything
here is about *how* to make a change land correctly — the *what* and *why* of the design
lives in `TECHNICAL.md`, and the macros carry their own constraints in comments at the top
of each `.bas` / `.FCMacro` file.

## The built frontend is committed — rebuild it or your change ships nothing

`EasyPDM.Api/wwwroot/` is in git, and `run.sh` only serves it; it never builds it. A change
under `EasyPDM.Web/src/` therefore does **nothing** in a running app, a Docker image or an
installer until the bundle is rebuilt and committed alongside the source:

```bash
cd EasyPDM.Web && npm run build      # writes into ../EasyPDM.Api/wwwroot/
```

The symptom when this is forgotten is confusing rather than obvious: the source clearly
contains the fix, the app clearly does not have it. If behaviour does not change after an
edit, check which bundle is actually being served before suspecting the code:

```bash
grep -o 'assets/index-[A-Za-z0-9_-]*\.js' EasyPDM.Api/wwwroot/index.html
curl -s http://localhost:5000/ | grep -o 'assets/index-[A-Za-z0-9_-]*\.js'
```

## Run the tests before pushing

```bash
dotnet test EasyPDM.Api.Tests/EasyPDM.Api.Tests.csproj
```

They need a reachable PostgreSQL and run against a **separate schema** (`pdm_test`), which
they drop and recreate each time — they never touch `public`. Point them elsewhere with
`EASYPDM_TEST_CONNECTION_STRING` if needed.

Checking an endpoint by hand with `curl` is not a substitute: a change once passed every
live check and still broke CI, because a test asserted on a hard-coded CSV string nobody
thought to look at.

## Commit messages in English

Switched from Polish; applies to everything from here on. Explain *why* the change is
correct, not just what moved — most of these messages are the only place a non-obvious
constraint is recorded.

## CHANGELOG entries stay on one line each

Never hard-wrap them, even though `README.md` and `TECHNICAL.md` in this repo are wrapped at
about 90 characters.

`.github/workflows/create-release-draft.yml` cuts the section for the tagged version straight
out of `CHANGELOG.md` with `awk` and hands it to `gh release create`. A `.md` file rendered in
the repo collapses single newlines into flowing text, so wrapping looks fine there — but
**release descriptions render as GFM, where every single newline becomes a `<br>`**. The notes
on v0.4.1, v0.4.2 and v0.4.3 are visibly ragged for exactly that reason.

Confirmed through GitHub's own renderer rather than assumed:

```bash
gh api -X POST /markdown -f mode=markdown -f text="one
two"    # -> <p>one\ntwo</p>    (flows)
gh api -X POST /markdown -f mode=gfm -f text="one
two"    # -> <p>one<br>two</p>  (breaks)
```

## Releasing

Pushing a `vX.Y.Z` tag builds the installers and opens a **draft** release; publishing it
stays manual on purpose. Before tagging, the version must match in both places or the
workflow refuses the build:

- `EasyPDM.Web/src/version.ts` — `APP_VERSION`
- `packaging/windows/EasyPDM.iss` — `MyAppVersion`

SolidWorks `.swp` macros are deliberately **not** attached by CI — they need compiling on a
live SolidWorks and are added to the draft by hand. When a release carries macro changes,
that step is what actually delivers them.
