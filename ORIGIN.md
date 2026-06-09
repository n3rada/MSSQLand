# 🥚 Origin

MSSQLand was born from real-world needs and hard-earned lessons.

## The Contribution

These features were not built for recognition. They came from real operational needs: scenarios where the existing tool simply could not do what was required in the field.

I originally contributed extensively to [SQLRecon](https://github.com/skahwah/SQLRecon), which provided a solid foundation for MS SQL post-exploitation. In [issue #16](https://github.com/skahwah/SQLRecon/issues/16#issuecomment-2048435229), the author stated that chained linked server traversal via `OPENQUERY` was not feasible. I implemented it anyway, along with proper sysadmin impersonation detection, cascading impersonation through linked server chains, markdown-formatted output, a `/debug` flag (which the author himself requested), and a full module refactoring that unified the duplicated `i`/`l`/`t` command families into a single intelligent pipeline. The result was [PR #17](https://github.com/skahwah/SQLRecon/pull/17): **20 commits, 1523 additions, 2850 deletions**, developed over two months of back-and-forth collaboration.

The author reviewed the PR over several weeks, acknowledged the work was solid, reported test failures which I fixed within hours, and even stated he had *"started building some changes on top of"* mine. The collaboration appeared healthy.

## What Went Wrong

### 1. Never Pushed Back to the PR

At no point did the author push his modifications back to the PR branch. GitHub explicitly supports this: a maintainer can commit directly to a contributor's PR branch, iterate on it, and merge, preserving everyone's authorship. He chose not to, deliberately keeping his work separate from mine.

### 2. Closed Without Merging

On July 3, 2024, instead of merging, he **closed the PR** and released SQLRecon v3.7 with the changes copy-pasted into his own commits, effectively erasing my contributions from Git history. The commit trail that should have carried my name was replaced by his alone.

### 3. Publicly Claimed Sole Credit

That same day, he [posted on X](https://x.com/sanjivkawa/status/1808275277325517186) (17K+ views, 200 reposts):

> *"**I've** made some long awaited updates to SQLRecon!"*

First person singular. No mention of any contributor. The very features demonstrated in the thread — linked server chaining, improved impersonation, unified module handling — were the core of my PR, closed that same day.

### 4. Dismissed the Contributor

When I pointed out that this erased my contribution history, his response was: *"I don't owe you anything"* and *"Grow up."* He suggested I *"add a space to the README"* if I wanted a contributor badge.

## How It Should Have Been Done

The proper workflow would have been straightforward:
1. Push modifications directly to the PR branch (preserving both authors' commits)
2. Or merge the PR into a `dev` branch (preserving commit authorship), then refactor on top with his own commits
3. Merge into `main` once satisfied

For comparison, [GraphSpy PR #18](https://github.com/RedByte1337/GraphSpy/pull/18) shows how this works in practice: I submitted a large refactoring (13 commits, 4308 additions, 2832 deletions), the repo owner [@RedByte1337](https://github.com/RedByte1337) pushed his own fixes and adjustments **directly to the PR branch** (fixing modules, adjusting log levels, HTML fixes), we iterated together reaching 30 commits from both authors, and he **merged the PR**. Full Git authorship preserved for everyone. That is the standard. That is respectful open-source collaboration.

## MSSQLand

Rather than let this work go to waste, I built MSSQLand from scratch. Beyond the contribution handling, SQLRecon had architectural limitations that made it unsuitable for real-world chaining scenarios.

Impersonation (`/i`) and linked servers (`/l`) are mutually exclusive. The code explicitly blocks combining them. In practice, this means you cannot impersonate a user on the initial server and then traverse linked servers, which is exactly the most common scenario. There is no cascading impersonation either: `/i` accepts a single username and prepends a single `EXECUTE AS LOGIN`. No per-hop impersonation exists in linked server chains: the chain is a flat list of server names with no associated security context. And every module duplicates the same four-way `switch` statement (`standard`/`impersonation`/`linked`/`chained`), making any new context combination a cross-cutting change across the entire codebase.

MSSQLand was designed from the ground up with a unified context model. Impersonation, linked server traversal, and database context are properties of each server in the chain, not mutually exclusive modes. The server notation `server/user1/user2@database` naturally expresses cascading impersonation at any hop, and the query service transparently wraps queries with the correct `EXECUTE AS` and `OPENQUERY`/`EXEC AT` nesting regardless of chain depth.

Here, no one will be erased from Git history.
