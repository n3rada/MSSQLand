# Contributing to MSSQLand 🫂

Thank you for considering contributing to MSSQLand! Your efforts help make this tool better for everyone, and every contribution is valued. This document outlines the guidelines for contributing to ensure a productive and fair collaboration.

## 🆕 Feature Requests

Have an idea for a new feature?

1. **Check discussions/issues** to see if it's already proposed
2. **Open a discussion** in [GitHub Discussions](https://github.com/n3rada/MSSQLand/discussions)
3. Describe:
   - The use case
   - Why it's useful
   - How it might work
   - Any potential challenges

## 🚀 Code Contributions

### Getting Started

1. **Fork the repository**
   ```bash
   git clone https://github.com/YOUR_USERNAME/MSSQLand.git
   cd MSSQLand
   ```

2. **Create a feature branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

3. **Make your changes** following the guidelines below

4. **Test your changes** thoroughly

5. **Sign your commits** (see below)

6. **Submit a pull request** with a clear description

### Code Guidelines

- **C# Version:** 11.0 targeting .NET Framework 4.8
- **Code Style:** Follow existing patterns (see `.editorconfig`)
- **Naming:** Use descriptive names, follow C# conventions
- **Comments:** Add XML documentation for public APIs, file path comments at the top of each file
- **Actions:** New actions should inherit from `BaseAction`
- **SQL Queries:** No comments in SQL strings (stealth requirement)

### Project Structure

```
MSSQLand/
├── Actions/          # All action implementations
│   ├── Administration/
│   ├── ConfigMgr/
│   ├── Database/
│   ├── Domain/
│   ├── Execution/
│   ├── FileSystem/
│   └── Remote/
├── Services/         # Core services (DB, Auth, etc.)
├── Models/           # Data models
├── Utilities/        # Helper classes
└── Exceptions/       # Custom exceptions
```

### Adding a New Action

1. Create a new class in the appropriate subfolder under `Actions/`
2. Inherit from `BaseAction`
3. Add file path comment at the top:
   ```csharp
   // MSSQLand/Actions/Category/YourAction.cs
   ```
4. Override `ValidateArguments()` and `Execute()`
5. Add XML documentation
6. Register in `ActionFactory.cs`
7. Update `.csproj` if needed

**Example:**
```csharp
// MSSQLand/Actions/Database/YourAction.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Brief description of what this action does.
    /// </summary>
    internal class YourAction : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Parameter description")]
        private string _parameter;

        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);
            _parameter = GetPositionalArgument(positionalArgs, 0, null);
            
            if (string.IsNullOrWhiteSpace(_parameter))
                throw new ArgumentException("Parameter is required");
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Executing YourAction");
            
            // Your implementation here
            
            return result;
        }
    }
}
```

### Testing

Before submitting:

1. **Build the project**
   ```bash
   dotnet build MSSQLand/MSSQLand.sln
   ```
2. **Test against a real SQL Server** instance
3. **Verify no warnings** during build
4. **Test both editions** (standard and ConfigMgr if applicable)
5. **Verify no regressions** in existing features

## Signing Your Commits 🗝️
To ensure accountability and transparency in contributions, always sign your commits. This helps verify the authenticity of the commit and ensures a clear history of who contributed what.

Follow [GitHub’s guide on signing commits](https://docs.github.com/en/authentication/managing-commit-signature-verification/signing-commits) to generate a GPG key and configure it for Git.

Signed commits ensure the integrity and authenticity of contributions, reflecting the highest standards of professionalism in the project.

## Handling of Pull Requests

Contributions are preserved transparently in Git history. No fear to have here—no one will copy-paste your code without adhering to the collaborative ethos of open-source.

If the code needs changes, it will be merged - yet accepted - into a specific branch to preserve the contributor's effort while allowing for refactoring and thorough review.

### PR Accepted ✅
- All commits will be retained in the repository, ensuring proper credit is given.
- Any modifications or refactors will be performed **after merging**, with clear documentation of changes.

### PR Rejected ❌
- Feedback will be provided with reasons for rejection.
- You are encouraged to resubmit after addressing the concerns.
- No one's contributions will be erased or dismissed without acknowledgment.

## 🐛 Reporting Issues

If you find a bug:
1. **Check existing issues** to avoid duplicates
2. **Create a new issue** with clear description, steps to reproduce, and environment details

## 🔒 Security Vulnerabilities

**Do not open public issues for security vulnerabilities.**

See [.github/SECURITY.md](.github/SECURITY.md) for how to report security issues responsibly.

## 📚 Documentation

- **Overview**: [README.md](./README.md)
- **Development Guide**: [.github/DEVELOPMENT.md](.github/DEVELOPMENT.md) - Architecture, structure, and how to add new actions
- **Release Process**: [.github/RELEASING.md](.github/RELEASING.md) - For maintainers

## ❓ Questions?

- **General questions:** [GitHub Discussions](https://github.com/n3rada/MSSQLand/discussions)
- **Bug reports:** [GitHub Issues](https://github.com/n3rada/MSSQLand/issues)
- **Security:** See [.github/SECURITY.md](.github/SECURITY.md)

---

Thank you for contributing to MSSQLand! 🎉





