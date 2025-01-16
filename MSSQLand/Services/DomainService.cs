using System.Collections.Generic;
using System.Collections;
using System.DirectoryServices;

namespace MSSQLand.Services
{
    internal sealed class DomainService
    {
        internal DirectoryEntry Directory { get; }

        internal DomainService()
        {
            Directory = new DirectoryEntry();
        }

        internal DomainService(string path)
        {
            Directory = new DirectoryEntry(path);
        }
    }

    internal sealed class Ldap
    {
        private readonly DomainService _searcher;

        internal Ldap(DomainService searcher)
        {
            _searcher = searcher;
        }

        /// <summary>
        /// The ExecuteLdapQuery method allows LDAP queries to be executed 
        /// against a domain controller.
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="properties"></param>
        /// <returns></returns>
        internal Dictionary<string, Dictionary<string, object[]>> ExecuteLdapQuery(string filter, string[] properties = null)
        {
            DirectorySearcher searcher = new DirectorySearcher(_searcher.Directory)
            {
                Filter = filter,
            };

            if (properties is not null)
            {
                searcher.PropertiesToLoad.AddRange(properties);
            }

            SearchResultCollection searchResultCollection = searcher.FindAll();

            Dictionary<string, Dictionary<string, object[]>> resultDictionary = new Dictionary<string, Dictionary<string, object[]>>();

            foreach (SearchResult searchResult in searchResultCollection)
            {
                resultDictionary.Add(searchResult.Path, null);

                Dictionary<string, object[]> dictionary = new Dictionary<string, object[]>();

                foreach (DictionaryEntry entry in searchResult.Properties)
                {
                    List<object> values = new List<object>();

                    foreach (object value in (ResultPropertyValueCollection)entry.Value)
                        values.Add(value);

                    dictionary.Add(entry.Key.ToString(), values.ToArray());
                }

                resultDictionary[searchResult.Path] = dictionary;
            }

            return resultDictionary;
        }
    }
}
