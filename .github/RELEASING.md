# MSSQLand Release Guide

> **📌 Note:** This guide is for project maintainers and contributors. Users looking for releases should visit [Releases](https://github.com/n3rada/MSSQLand/releases) to download binaries.

## 📋 Versioning Strategy

MSSQLand follows **Semantic Versioning 2.0.0** (https://semver.org/)

### Version Format: `MAJOR.MINOR.PATCH`

```
v2.1.3
│ │ │
│ │ └─ PATCH: Bug fixes, small corrections (v2.1.0 → v2.1.1)
│ └─── MINOR: New features, backward compatible (v2.0.0 → v2.1.0)
└───── MAJOR: Breaking changes, incompatible API (v1.0.0 → v2.0.0)
```

### When to Increment Each Number

#### MAJOR (Breaking Changes)
- Removing existing actions
- Changing command-line arguments in incompatible ways
- Removing or renaming output formats
- Major architectural changes affecting usage

**Example:** `2.0.0` → `3.0.0`

#### MINOR (New Features)
- Adding new actions (e.g., new ConfigMgr commands)
- Adding new optional arguments
- Adding new authentication methods
- Enhancing existing features without breaking compatibility

**Example:** `2.1.0` → `2.2.0`

#### PATCH (Bug Fixes)
- Fixing bugs
- Performance improvements
- Documentation updates
- Code refactoring without behavior changes

**Example:** `2.1.0` → `2.1.1`

---

## 🏷️ Release Naming Convention

### Git Tags
```bash
v2.1.0          # Official release
v2.1.0-beta.1   # Beta/pre-release
v2.1.0-rc.1     # Release candidate
```

### GitHub Release Titles
```
v2.1.0 - "Code Name" (YYYY-MM-DD)
```

---

## 🚀 Release Process

### Step 1: Update Version Number

Edit `MSSQLand/Properties/AssemblyInfo.cs`:

```csharp
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
```

### Step 2: Commit Changes

```bash
git add MSSQLand/Properties/AssemblyInfo.cs CHANGELOG.md
git commit -m "Bump version to 2.1.0"
git push origin main
```

### Step 3: Create and Push Tag

```bash
# Create annotated tag with message
git tag -a v2.1.0 -m "Release v2.1.0 - Enhanced Edition

Added:
- Data access control for linked servers
- EAFP pattern implementation
- Improved error handling

Changed:
- SQL comment handling
- Build system improvements"

# Push tag to GitHub (triggers release build)
git push origin v2.1.0
```

### Step 4: Edit GitHub Release

After the automated build completes:

1. Go to **GitHub → Releases**
2. Find the auto-created release
3. Click **Edit**
4. Update the release notes
5. Click **Publish release**

---

---

## 🛠️ Quick Commands

### Manual Release
```bash
# 1. Update version in AssemblyInfo.cs
# 2. Commit
git add .
git commit -m "Release v2.1.0"

# 3. Tag
git tag -a v2.1.0 -m "Release v2.1.0"

# 4. Push
git push origin main
git push origin v2.1.0
```

### View Tags
```bash
git tag -l
git show v2.1.0
```

### Delete Tag (if needed)
```bash
# Local
git tag -d v2.1.0

# Remote
git push origin --delete v2.1.0
```
