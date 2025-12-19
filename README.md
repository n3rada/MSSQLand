# ✈️ MSSQLand

Land gracefully in your target Microsoft SQL Server (MS SQL) DBMS, as if arriving on a business-class flight with a champagne glass in hand. 🥂

<p align="center">
    <img width="350" src="/media/MSSQLand__icon-nobg.png" alt="MSSQLand Logo">
</p>

MSSQLand is built for interacting with [Microsoft SQL Server](https://en.wikipedia.org/wiki/Microsoft_SQL_Server) database management system (DBMS) during your red team activities or any security audit. Designed **for constrained environments** where operations must be executed directly through your beacons, **using assembly execution** it allows you to pave your way across multiple linked servers and impersonate whoever you can along the way, emerging from the last hop with any desired action.

> [!TIP]
> MSSQLand is built using `.NET Framework 4.8`, with assembly execution in mind. If you need to connect using Kerberos ticket or NT/LM hashes, go with [mssqlclient-ng](https://github.com/n3rada/mssqlclient-ng), the `Python3` version built with external access from Unix in mind.

> [!NOTE]
> Do not forget the basics. During a security assessment, it is sometimes easier to use [SQL Server Management Studio (SSMS)](https://learn.microsoft.com/en-us/ssms/).

## 🧸 Usage

```shell
MSSQLand.exe <host> [options] <action> [action-options]
```

> [!TIP]
> Avoid typing out all the **[RPC Out](https://learn.microsoft.com/fr-fr/sql/t-sql/functions/openquery-transact-sql)** or **[OPENQUERY](https://learn.microsoft.com/fr-fr/sql/t-sql/functions/openquery-transact-sql)** calls manually. Let the tool handle any linked servers chain with the `-l` argument, so you can focus on the big picture.

Format: `server:port/user@database` or any combination `server/user@database:port`.
- `server` (required) - The SQL Server hostname or IP
- `:port` (optional) - Port number (default: 1433, also common: 1434, 14333, 2433)
- `/user` (optional) - User to impersonate on this server ("execute as user")
- `@database` (optional) - Database context (defaults to 'master' if not specified)

```shell
MSSQLand.exe localhost -c token info
MSSQLand.exe localhost:1434@db03 -c token info
```

### 🔗 Linked Servers Chain

Chain multiple SQL servers using the `-l` flag with **semicolon (`;`) as the separator**:

```shell
-l SQL01;SQL02/user;SQL03@database
```

**Syntax:**
- **Semicolon (`;`)** - Separates servers in the chain
- **Forward slash (`/`)** - Specifies user to impersonate ("execute as user")
- **At sign (`@`)** - Specifies database context
- **Brackets (`[...]`)** - Used to protect the server name from being split by our delimiters

**Examples:**
```shell
# Simple chain
-l SQL01;SQL02;SQL03

# With impersonation and databases
-l SQL01/admin;SQL02;SQL03/manager@clients

# Server names can contain hyphens, dots (no brackets needed)
-l SQL-01;SERVER.001;HOST.DOMAIN.COM

# Brackets only needed if server name contains delimiter characters
-l [SERVER;PROD];SQL02;[SQL03@clients]@clientdb
```

> [!NOTE]
> Port specification (`:port`) only applies to the initial host connection. Linked server chains (`-l`) use the linked server names as configured in `sys.servers`, not `hostname:port` combinations.


### 

> [!NOTE]
> Port specification (`,port`) only applies to the initial host connection. Linked server chains (`-l`) use the linked server names as configured in `sys.servers`, not `hostname:port` combinations.


## 🫤 Help

- `-h` or `--help` - Show all available actions
- `-h search_term` - Filter actions (e.g., `-h adsi` shows all ADSI-related actions)
- `localhost -c token createuser -h` - Show detailed help for a specific action

## 📸 Clean Output for Clean Reports

The tool's output, enriched with timestamps and valuable contextual information, is designed to produce visually appealing and professional results, making it ideal for capturing high-quality screenshots for any of your reports (e.g., customer deliverable, internal report, red team assessments).

All output tables are Markdown-friendly and can be copied and pasted directly into your notes without any formatting hassle.

> [!TIP]
> You can also have `.csv` compatible output by using the `-o csv` option: `MSSQLand.exe localhost -c token -o csv --silent procedures > procedures.csv`

## 🤝 Contributing 

Contributions are welcome and appreciated! Whether it's fixing bugs, adding new features, improving the documentation, or sharing feedback, your effort is valued and makes a difference.
Open-source thrives on collaboration and recognition. Contributions, large or small, help improve the tool and its community. Your time and effort are truly valued. 

Here, no one will be erased from Git history. No fear to have here. No one will copy-paste your code without adhering to the collaborative ethos of open-source.

Please see the [CONTRIBUTING.md](./CONTRIBUTING.md) for detailed guidelines on how to get started.

## 🥚 Origin

MSSQLand was born from real-world needs and hard-earned lessons.

Originally, I contributed extensively to [SQLRecon](https://github.com/skahwah/SQLRecon), which provided a solid foundation for MS SQL post-exploitation and reconnaissance. However, during my contributions to SQLRecon, particularly in addressing [chained linked server traversal](https://github.com/skahwah/SQLRecon/issues/16#issuecomment-2048435229) and enhancing user impersonation, I encountered significant roadblocks in how contributions were handled. [My pull request](https://github.com/skahwah/SQLRecon/pull/17), which introduced major improvements in impersonation, chaining, and context management, was ultimately not merged but copy pasted.

Rather than let this work go to waste, I decided to develop MSSQLand, an OOP-driven, modular, and community-friendly alternative. Unlike SQLRecon, which required deep refactoring to make simple modifications, MSSQLand was built with developers in mind. The tool is built with extensibility in mind, allowing integration of new features while maintaining clarity and simplicity. It aims to provide a structured, customizable, and operator-friendly experience for engagements requiring MS SQL exploitation.

## ⚠️ Disclaimer

**This tool is provided strictly for defensive security research, education, and authorized penetration testing.** You must have **explicit written authorization** before running this software against any system you do not own.

This tool is designed for educational purposes only and is intended to assist security professionals in understanding and testing the security of SQL Server environments in authorized engagements.

Acceptable environments include:
- Private lab environments you control (local VMs, isolated networks).  
- Sanctioned learning platforms (CTFs, Hack The Box, OffSec exam scenarios).  
- Formal penetration-test or red-team engagements with documented customer consent.

Misuse of this project may result in legal action.

## ⚖️ Legal Notice
Any unauthorized use of this tool in real-world environments or against systems without explicit permission from the system owner is strictly prohibited and may violate legal and ethical standards. The creators and contributors of this tool are not responsible for any misuse or damage caused.

Use responsibly and ethically. Always respect the law and obtain proper authorization.
