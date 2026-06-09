# 🥚 Origin

MSSQLand exists because of a real frustration.

## The Contribution

These features came from operational needs, not from wanting recognition. The existing tool could not handle the scenarios I was running into.

I contributed to [SQLRecon](https://github.com/skahwah/SQLRecon), a capable MS SQL post-exploitation tool. In [issue #16](https://github.com/skahwah/SQLRecon/issues/16#issuecomment-2048435229), the author said chained linked server traversal via `OPENQUERY` was not feasible. I implemented it anyway, along with proper sysadmin impersonation detection, cascading impersonation through linked server chains, markdown-formatted output, a `/debug` flag (which the author himself requested), and a full module refactoring that unified the duplicated `i`/`l`/`t` command families into a single pipeline. The result was [PR #17](https://github.com/skahwah/SQLRecon/pull/17): **20 commits, 1523 additions, 2850 deletions**, developed over two months of back-and-forth.

The author spent weeks reviewing, said the work was solid, reported test failures which I fixed within hours, and told me he had *"started building some changes on top of"* mine.

## What Went Wrong

### 1. Never Pushed Back to the PR

The author never pushed his modifications to the PR branch. GitHub supports this directly: a maintainer can commit to a contributor's branch, iterate, and merge, keeping everyone's authorship intact. He did not do that.

### 2. Closed Without Merging

On July 3, 2024, he **closed the PR** and released SQLRecon v3.7 with the changes copied into his own commits. My name was not in that history.

### 3. Publicly Claimed Sole Credit

That same day, he [posted on X](https://x.com/sanjivkawa/status/1808275277325517186) (17K+ views, 200 reposts):

> *"**I've** made some long awaited updates to SQLRecon!"*

No mention of any contributor. The features in that thread, linked server chaining, improved impersonation, unified module handling, were the core of the PR he had just closed.

### 4. Dismissed the Contributor

When I raised this, his response was: *"I don't owe you anything"* and *"Grow up."* He suggested I *"add a space to the README"* if I wanted a contributor badge.

## How It Should Have Been Done

Simple options were available:
1. Push modifications directly to the PR branch, preserving both authors' commits
2. Merge the PR into a `dev` branch, then refactor on top
3. Merge into `main` once satisfied

[GraphSpy PR #18](https://github.com/RedByte1337/GraphSpy/pull/18) is what this looks like in practice: I submitted a large refactoring (13 commits, 4308 additions, 2832 deletions), the repo owner [@RedByte1337](https://github.com/RedByte1337) pushed fixes and adjustments **directly to the PR branch**, we iterated together reaching 30 commits from both authors, and he **merged it**. Everyone's authorship is in the history. That is how it works.

## MSSQLand

I built MSSQLand from scratch. Partly because of how the contribution was handled, but also because SQLRecon had real architectural problems that made it the wrong base for what I needed.

Impersonation (`/i`) and linked servers (`/l`) are mutually exclusive. The code explicitly blocks combining them. In practice, you cannot impersonate a user on the initial server and then traverse linked servers, which is the most common scenario. There is no cascading impersonation either: `/i` accepts one username and prepends a single `EXECUTE AS LOGIN`. No per-hop impersonation exists in linked server chains: the chain is a flat list of server names with no associated security context. And every module duplicates the same four-way `switch` statement (`standard`/`impersonation`/`linked`/`chained`), so any new context combination requires changes across the whole codebase.

MSSQLand uses a unified context model. Impersonation, linked server traversal, and database context are properties of each server in the chain, not separate modes. The notation `server/user1/user2@database` expresses cascading impersonation at any hop, and the query service handles the correct `EXECUTE AS` and `OPENQUERY`/`EXEC AT` nesting at any depth.

Here, no one will be erased from Git history.

The architecture behind these decisions is documented in [DEVELOPMENT.md](DEVELOPMENT.md).
