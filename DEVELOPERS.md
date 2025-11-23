
## 🏗️ Architecture

MSSQLand is built on a clean, OOP-driven architecture designed for extensibility.

- **Modular Actions**: Each action is a self-contained class inheriting from [`BaseAction`](.\MSSQLand\Actions\BaseAction.cs).
- **Factory Pattern**: Actions are automatically discovered and instantiated via [`ActionFactory`](.\MSSQLand\Actions\ActionFactory.cs).
- **[Service](.\MSSQLand\Services) Layer**: Separation of concerns with `DatabaseContext`, `QueryService`, `UserService`, and `AuthenticationService`.
- **Credential Abstraction**: Multiple authentication methods through [`CredentialsFactory`](.\MSSQLand\Services\Authentication\Credentials\CredentialsFactory.cs) (Token, Domain, Local, Azure, Entra ID, Windows Auth).
- **Chainable Operations**: Built-in support for linked server traversal and user impersonation chaining.


## 🎬 New Action

This design makes adding new features straightforward—simply create a new action class, and the framework handles the rest.

Depending of your new feature, you will need to create a new action class inside the targeted sub-directory. For example, if it is related to Active Directory, you can add-it inside [Domain](.\MSSQLand\Actions\Domain). Once decided, you can next copy-paste this skeleton:

```csharp
using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Domain
{
    internal class NewAction : BaseAction
    {
        // Positional arguments with metadata
        [ArgumentMetadata(Position = 0, ShortName = "a", LongName = "argument", Required = false, Description = "Describe what this argument does")]
        private string _argument;

        // Internal properties not exposed as arguments
        [ExcludeFromArguments]
        private string _privateVariable;

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                // No arguments provided - use defaults or throw exception if required
                return;
            }

            // Parse both positional and named arguments
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get argument from position 0 or /a: or /argument:
            _argument = GetNamedArgument(namedArgs, "a")
                     ?? GetNamedArgument(namedArgs, "argument")
                     ?? GetPositionalArgument(positionalArgs, 0);

            // Validate argument if needed
            if (string.IsNullOrEmpty(_argument))
            {
                throw new ArgumentException("Argument is required. Example: /a:newaction myvalue");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Log the action being performed
            Logger.TaskNested("Performing new action operation");

            // Your T-SQL query
            string query = @"
                SELECT 
                    column1,
                    column2
                FROM sys.some_table
                WHERE condition = @param;";

            try
            {
                // Execute query and get results
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No results found.");
                    return null;
                }

                // Display results
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                
                // Log success
                Logger.Success($"Action completed successfully. Retrieved {result.Rows.Count} row(s).");

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing action: {ex.Message}");
                return null;
            }
        }
    }
}
```

Then, do not forget to add the newly created action to the [factory](MSSQLand\Actions\ActionFactory.cs):

```csharp
{ "new", (typeof(NewAction), "Lorem ipsum dolor sit amet consectetur adipiscing elit quisque faucibus ex sapien vitae pellentesque sem.") },
```

And voilà, now you can use-it directly with:
```shell
/a:new
```

## 🌉 Port Forwarding with Linux

If you're running MSSQLand on your Windows host but need to access a SQL Server target through a Linux environment (Hyper-V VM, VMware, or WSL), you can easily forward the connection using `socat`:

```bash
sudo socat TCP4-LISTEN:1433,fork,reuseaddr TCP:10.10.11.90:1433
```

This command listens on port 1433 on your Linux machine and forwards all traffic to the target SQL Server at `10.10.11.90:1433`. You can then connect MSSQLand to your Linux VM's IP from your Windows host.
