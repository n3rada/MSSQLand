# Security Policy

## 🔒 Reporting Security Vulnerabilities

**Please do not report security vulnerabilities through public GitHub issues.**

We take security seriously. If you discover a security vulnerability in MSSQLand, please report it responsibly.

### How to Report

**Email:** [Create a private security advisory](https://github.com/n3rada/MSSQLand/security/advisories/new) or contact via GitHub private message.

**Include in your report:**
1. **Type of vulnerability** (e.g., SQL injection, privilege escalation, information disclosure)
2. **Location** (file path, line number if possible)
3. **Step-by-step reproduction** instructions
4. **Proof of concept** code (if applicable)
5. **Potential impact** (what an attacker could achieve)
6. **Suggested fix** (if you have one)

**Example Report:**
```
Subject: [Security] SQL Injection in AdsiQuery action

Type: SQL Injection
Location: MSSQLand/Actions/Remote/AdsiQuery.cs, line 123
Severity: High

Description:
User input is not properly sanitized before being concatenated into SQL query,
allowing potential SQL injection.

Reproduction Steps:
1. Connect to SQL Server with credentials
2. Run: MSSQLand.exe server.local adsiquery "'; DROP TABLE users; --"
3. Observe SQL injection payload execution

Impact:
An attacker could execute arbitrary SQL commands, potentially leading to:
- Data exfiltration
- Database modification
- Privilege escalation

Suggested Fix:
Use parameterized queries or proper input sanitization before building the query.
```

## 🕐 Response Timeline

- **Initial Response:** Within 48 hours
- **Status Update:** Within 7 days
- **Fix Timeline:** Depends on severity
  - **Critical:** Emergency patch within 1-3 days
  - **High:** Patch within 1-2 weeks
  - **Medium:** Patch in next regular release
  - **Low:** Addressed when possible

## 🎯 Scope

### In Scope
- SQL injection vulnerabilities
- Authentication bypass
- Privilege escalation
- Information disclosure
- Remote code execution (unintended)
- Credential leakage
- Logic flaws leading to security issues

### Out of Scope
- Social engineering attacks
- Physical security issues
- DoS attacks (this is an offensive security tool)
- Issues in third-party dependencies (report to them directly)
- **Expected behavior:** MSSQLand is designed for SQL Server exploitation, so issues like "allows command execution" are expected features, not vulnerabilities

## 🏆 Recognition

We appreciate security researchers who responsibly disclose vulnerabilities:

- **Credit:** We'll acknowledge you in release notes (if you wish)
- **Hall of Fame:** Listed in [CONTRIBUTORS.md](../CONTRIBUTORS.md) (optional)
- **Coordinated Disclosure:** We'll work with you on disclosure timing

## ⚠️ Responsible Use

MSSQLand is designed for:
- **Authorized penetration testing**
- **Red team operations** with written permission
- **Security research** on your own systems
- **Educational purposes** in controlled environments

**Do not use MSSQLand:**
- Against systems without authorization
- For malicious purposes
- To violate laws or regulations
- In production environments without approval

## 📚 Security Best Practices for Users

When using MSSQLand:

1. **Secure Your Credentials**
   - Don't hardcode passwords in scripts
   - Use secure credential storage
   - Clear command history after use

2. **Secure Your Findings**
   - Encrypt reports containing sensitive data
   - Use secure channels for communication
   - Follow your organization's data handling policies

3. **Audit Logging**
   - MSSQLand actions may trigger alerts
   - Be aware of logging and monitoring
   - Coordinate with blue team if applicable

4. **Network Security**
   - Use VPNs for remote testing
   - Be cautious on shared networks
   - Consider network segmentation

## 🔐 Security Features

MSSQLand includes security-conscious design:

- **No telemetry:** No data sent to external servers
- **Local execution:** All operations performed locally
- **Credential handling:** Credentials cleared from memory when possible
- **Error messages:** Avoid leaking sensitive info in logs
- **SQL comments removed:** Queries sanitized before execution (stealth)

## 📖 Security Resources

- [OWASP SQL Injection Prevention](https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html)
- [Microsoft SQL Server Security Best Practices](https://learn.microsoft.com/en-us/sql/relational-databases/security/)
- [SANS Penetration Testing Resources](https://www.sans.org/blog/)

## 🤝 Disclosure Policy

We follow **coordinated disclosure**:

1. You report the vulnerability privately
2. We acknowledge and investigate
3. We develop and test a fix
4. We coordinate disclosure timing with you
5. We release the fix and advisory simultaneously
6. You may publish your findings after the fix is public

**Typical disclosure window:** 90 days from report, unless circumstances require faster action.

## 📞 Contact

For security concerns only:
- **GitHub Security Advisories:** https://github.com/n3rada/MSSQLand/security/advisories/new
- **Non-security issues:** Use [GitHub Issues](https://github.com/n3rada/MSSQLand/issues)

---

Thank you for helping keep MSSQLand and its users secure! 🛡️
