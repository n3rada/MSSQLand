Thank you for considering contributing to MSSQLand! Your efforts help make this tool better for everyone, and every contribution is valued. This document outlines the guidelines for contributing to ensure a productive and fair collaboration.

## Getting Started

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

## Code Guidelines

- **C# Version:** 11.0 targeting .NET Framework 4.8
- **Naming:** Use descriptive names, follow C# conventions
- **Comments:** Add XML documentation for public APIs, file path comments at the top of each file
- **Actions:** New actions should inherit from `BaseAction`
- **SQL Queries:** No comments in SQL strings (stealth requirement)

## Testing

Before submitting:

1. **Build the project**
   ```bash
   dotnet build MSSQLand/MSSQLand.sln
   ```
2. **Test against a real SQL Server** instance
3. **Verify no warnings** during build
4. **Verify no regressions** in existing features

## Signing Your Commits 🗝️

To ensure accountability and transparency in contributions, always sign your commits. This helps verify the authenticity of the commit and ensures a clear history of who contributed what.

Follow [GitHub’s guide on signing commits](https://docs.github.com/en/authentication/managing-commit-signature-verification/signing-commits) to generate a GPG key and configure it for Git.

## Handling of Pull Requests

Contributions are preserved transparently in Git history. No fear to have here—no one will copy-paste your code without adhering to the collaborative ethos of open-source.

If the code needs changes, it will be merged into a specific branch to preserve the contributor's effort while allowing for refactoring and thorough review.

## 🐛 Reporting Issues

If you find a bug:
1. **Check existing issues** to avoid duplicates
2. **Create a new issue** with clear description, steps to reproduce, and environment details





