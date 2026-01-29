// MSSQLand/Utilities/Banner.cs

using System;
using System.Reflection;

namespace MSSQLand.Utilities
{
    internal class Banner
    {
        /// <summary>
        /// Displays the MSSQLand ASCII art banner.
        /// </summary>
        public static void Show()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            
            Console.WriteLine(@$"
  __  __  _____ _____  ____  _                     _ 
 |  \/  |/ ____/ ____|/ __ \| |                   | |
 | \  / | (___| (___ | |  | | |     __ _ _ __   __| |
 | |\/| |\___ \\___ \| |  | | |    / _` | '_ \ / _` |
 | |  | |____) |___) | |__| | |___| (_| | | | | (_| |
 |_|  |_|_____/_____/ \___\_\______\__,_|_| |_|\__,_|
 @n3rada                                       {version}                                                  
");
        }
    }
}
