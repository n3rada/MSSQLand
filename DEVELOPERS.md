
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
using System;


namespace MSSQLand.Actions.Domain
{
    internal class NewAction : BaseAction
    {

        // Positional Argument
        [ArgumentMetadata(Position = 0, Description = "Describe what is this argument")]
        private string _pos0;

        [ExcludeFromArguments]
        private string _privateArgument;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                // No arguments
                return;
            }

            string[] parts = SplitArguments(additionalArguments);

        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Your T-SQL query
            string query = @"";

            DataTable answer = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(OutputFormatter.ConvertDataTable(answer));

            return answer;
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
