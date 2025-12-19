using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace MSSQLand.Actions.FileSystem
{
    /// <summary>
    /// Display directory tree structure in Linux tree-style format using xp_dirtree.
    /// 
    /// This action uses the undocumented but widely-used xp_dirtree extended procedure
    /// to enumerate directories and files on the SQL Server filesystem. The output is
    /// formatted to match the Linux 'tree' command style.
    /// 
    /// The tree representation uses Unicode box-drawing characters by default:
    /// - ├── for intermediate items
    /// - └── for the last item in a directory
    /// - │   for vertical lines continuing to subdirectories
    /// - Indentation to show hierarchy levels
    /// 
    /// Use --unicode:0 or -u:false flag to fall back to ASCII characters (|, \, |) for legacy terminals
    /// 
    /// Note: Paths containing spaces must be enclosed in quotes.
    /// Examples:
    ///     tree "C:\Program Files" 3
    ///     tree "C:\My Documents" --depth:5
    ///     tree C:\Windows 2 --files:false
    /// </summary>
    internal class Tree : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = false, Description = "Directory path to display (default: current directory)")]
        private string _path;

        [ArgumentMetadata(Position = 1, ShortName = "d", LongName = "depth", Description = "Directory depth to traverse (1-255)")]
        private int _depth = 3;

        [ArgumentMetadata(Position = 2, ShortName = "f", LongName = "files", Description = "Show files (1|0 or true|false)")]
        private bool _showFiles = true;

        [ArgumentMetadata(Position = 3, ShortName = "u", LongName = "unicode", Description = "Use Unicode box-drawing characters (default: true, set to false for ASCII)")]
        private bool _useUnicode = true;

        /// <summary>
        /// Validates the arguments passed to the Tree action.
        /// </summary>
        /// <param name="args">Path and optional parameters.</param>
        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get path from positional argument or use current directory
            _path = GetPositionalArgument(positionalArgs, 0, ".");
            if (string.IsNullOrWhiteSpace(_path))
            {
                _path = ".";
            }

            // Get depth from named argument or positional argument
            string depthStr = GetNamedArgument(namedArgs, "depth", 
                              GetNamedArgument(namedArgs, "d", 
                              GetPositionalArgument(positionalArgs, 1, "3")));

            if (!int.TryParse(depthStr, out _depth))
            {
                Logger.Warning($"Invalid depth value '{depthStr}', using default depth of 3. " +
                             "If your path contains spaces, enclose it in quotes (e.g., \"C:\\Program Files\")");
                _depth = 3;
            }

            if (_depth < 1 || _depth > 255)
            {
                throw new ArgumentException("Depth must be between 1 and 255");
            }

            // Get show files flag from named argument or positional argument
            string filesStr = GetNamedArgument(namedArgs, "files", 
                              GetNamedArgument(namedArgs, "f", 
                              GetPositionalArgument(positionalArgs, 2, "true")));

            _showFiles = filesStr.Trim().ToLower() switch
            {
                "1" => true,
                "0" => false,
                "true" => true,
                "false" => false,
                "yes" => true,
                "no" => false,
                _ => true
            };

            // Get Unicode mode flag from named argument or positional argument
            string unicodeStr = GetNamedArgument(namedArgs, "unicode", 
                                GetNamedArgument(namedArgs, "u", 
                                GetPositionalArgument(positionalArgs, 3, "true")));

            _useUnicode = unicodeStr.Trim().ToLower() switch
            {
                "1" => true,
                "0" => false,
                "true" => true,
                "false" => false,
                "yes" => true,
                "no" => false,
                _ => true
            };
        }

        /// <summary>
        /// Executes the Tree action to display directory structure.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext instance to execute the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Displaying tree for: {_path}");
            Logger.Info($"Depth: {_depth}, Show files: {_showFiles}, Mode: {(_useUnicode ? "Unicode" : "ASCII")}");

            // Ensure path ends with backslash for xp_dirtree
            string path = _path.TrimEnd('\\') + "\\";

            // Escape single quotes in path
            string escapedPath = path.Replace("'", "''");

            // xp_dirtree parameters:
            // @path: Directory path
            // @depth: How many levels deep to traverse (default 0 = all)
            // @file: 1 = show files, 0 = directories only (default 0)
            int fileFlag = _showFiles ? 1 : 0;

            // Create temporary table to store results
            string query = $@"
CREATE TABLE #TreeResults (
    subdirectory NVARCHAR(512),
    depth INT,
    isfile BIT
);

INSERT INTO #TreeResults (subdirectory, depth, isfile)
EXEC xp_dirtree '{escapedPath}', {_depth}, {fileFlag};

SELECT subdirectory, depth, isfile FROM #TreeResults;

DROP TABLE #TreeResults;
";

            try
            {
                DataTable results = databaseContext.QueryService.ExecuteTable(query);

                if (results == null || results.Rows.Count == 0)
                {
                    Logger.Warning("No files or directories found");
                    Logger.NewLine();
                    Console.WriteLine(_path);
                    Logger.NewLine();
                    Console.WriteLine("0 directories, 0 files");
                    return null;
                }

                Logger.Debug($"Total results: {results.Rows.Count}");

                // Build the tree structure
                string treeOutput = BuildTree(results, _path);

                // Count statistics - only count items within depth limit
                int dirCount = 0;
                int fileCount = 0;

                foreach (DataRow row in results.Rows)
                {
                    bool isFile = row["isfile"] != DBNull.Value && Convert.ToBoolean(row["isfile"]);
                    int depth = row["depth"] != DBNull.Value ? Convert.ToInt32(row["depth"]) : 1;

                    if (depth <= _depth)
                    {
                        if (isFile)
                        {
                            fileCount++;
                        }
                        else
                        {
                            dirCount++;
                        }
                    }
                }

                Logger.NewLine();
                Console.WriteLine(treeOutput);
                Logger.NewLine();

                string stats = $"{dirCount} directories";
                if (_showFiles)
                {
                    stats += $", {fileCount} files";
                }
                Console.WriteLine(stats);

                return treeOutput;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to generate tree for '{_path}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Build a tree representation from xp_dirtree results.
        /// </summary>
        /// <param name="results">DataTable with subdirectory, depth, and isfile columns.</param>
        /// <param name="rootPath">The root path being displayed.</param>
        /// <returns>Tree representation as a string.</returns>
        private string BuildTree(DataTable results, string rootPath)
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine(rootPath);

            // Group results by depth and path
            List<TreeNode> treeStructure = OrganizeTreeStructure(results);

            // Build tree recursively
            if (treeStructure.Count > 0)
            {
                RenderTree(treeStructure, "", output, isLast: true);
            }

            return output.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// Organize flat xp_dirtree results into a hierarchical structure.
        /// 
        /// xp_dirtree returns:
        /// - subdirectory: just the name (not full path)
        /// - depth: level from root (1, 2, 3...)
        /// - isfile: 1 for file, 0 for directory
        /// 
        /// We need to track parent-child relationships using depth levels.
        /// </summary>
        /// <param name="results">Flat list of results from xp_dirtree.</param>
        /// <returns>Hierarchical tree structure.</returns>
        private List<TreeNode> OrganizeTreeStructure(DataTable results)
        {
            List<TreeNode> tree = new List<TreeNode>();
            Dictionary<int, TreeNode> depthStack = new Dictionary<int, TreeNode>();

            foreach (DataRow row in results.Rows)
            {
                string name = row["subdirectory"] != DBNull.Value ? row["subdirectory"].ToString() : "";
                int depth = row["depth"] != DBNull.Value ? Convert.ToInt32(row["depth"]) : 1;
                bool isFile = row["isfile"] != DBNull.Value && Convert.ToBoolean(row["isfile"]);

                // Skip items beyond the requested depth
                if (depth > _depth)
                {
                    continue;
                }

                TreeNode node = new TreeNode
                {
                    Name = name,
                    Depth = depth,
                    IsFile = isFile,
                    Children = new List<TreeNode>()
                };

                if (depth == 1)
                {
                    // Top-level item - add to tree root
                    tree.Add(node);
                    depthStack[1] = node;
                }
                else
                {
                    // Child item - find parent at previous depth level
                    if (depthStack.ContainsKey(depth - 1))
                    {
                        TreeNode parent = depthStack[depth - 1];
                        parent.Children.Add(node);
                        depthStack[depth] = node;
                    }
                    else
                    {
                        // This shouldn't happen with proper xp_dirtree output
                        Logger.Warning($"Parent at depth {depth - 1} not found for: {name} at depth {depth}");
                    }
                }
            }

            return tree;
        }

        /// <summary>
        /// Recursively render the tree structure with proper formatting.
        /// </summary>
        /// <param name="nodes">List of nodes at current level.</param>
        /// <param name="prefix">Current line prefix (for indentation).</param>
        /// <param name="output">StringBuilder to accumulate output lines.</param>
        /// <param name="isLast">Whether this is the last node at current level.</param>
        private void RenderTree(List<TreeNode> nodes, string prefix, StringBuilder output, bool isLast)
        {
            // Sort: directories first, then files, alphabetically
            var sortedNodes = nodes.OrderBy(n => n.IsFile).ThenBy(n => n.Name.ToLower()).ToList();

            for (int i = 0; i < sortedNodes.Count; i++)
            {
                TreeNode node = sortedNodes[i];
                bool isLastNode = (i == sortedNodes.Count - 1);

                // Determine the connector based on Unicode mode
                string connector;
                string newPrefix;

                if (_useUnicode)
                {
                    // Unicode box-drawing characters (default)
                    if (isLastNode)
                    {
                        connector = "└── ";
                        newPrefix = prefix + "    ";
                    }
                    else
                    {
                        connector = "├── ";
                        newPrefix = prefix + "│   ";
                    }
                }
                else
                {
                    // ASCII-compatible characters (fallback for legacy terminals)
                    if (isLastNode)
                    {
                        connector = "\\-- ";
                        newPrefix = prefix + "    ";
                    }
                    else
                    {
                        connector = "|-- ";
                        newPrefix = prefix + "|   ";
                    }
                }

                // Add file/directory indicator
                string displayName = node.IsFile ? node.Name : node.Name + "/";
                output.AppendLine($"{prefix}{connector}{displayName}");

                // Recursively render children
                if (node.Children.Count > 0)
                {
                    RenderTree(node.Children, newPrefix, output, isLast: isLastNode);
                }
            }
        }

        /// <summary>
        /// Internal class to represent a node in the tree structure.
        /// </summary>
        private class TreeNode
        {
            public string Name { get; set; }
            public int Depth { get; set; }
            public bool IsFile { get; set; }
            public List<TreeNode> Children { get; set; }
        }
    }
}
