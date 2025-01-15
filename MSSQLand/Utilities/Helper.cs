using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;


namespace MSSQLand.Utilities
{
    internal class Helper
    {

        public static void Show()
        {
            Logger.Banner("Available Actions");

            var actions = ActionFactory.GetAvailableActions();

            // Build a DataTable for the actions
            DataTable actionsTable = new();
            actionsTable.Columns.Add("Action", typeof(string));
            actionsTable.Columns.Add("Description", typeof(string));

            foreach (var action in actions)
            {
                actionsTable.Rows.Add(action.Key, action.Value.Description);
            }

            // Use MarkdownFormatter to display the table
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(actionsTable));
        }
    }
}
