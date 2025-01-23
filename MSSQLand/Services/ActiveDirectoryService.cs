using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;

namespace MSSQLand.Services
{
    /// <summary>
    /// Represents a service for interacting with a domain directory.
    /// </summary>
    internal sealed class ADirectoryService
    {
        /// <summary>
        /// The directory entry used for LDAP operations.
        /// </summary>
        internal DirectoryEntry Entry { get; }

        /// <summary>
        /// Initializes the service with the default directory entry.
        /// </summary>
        internal ADirectoryService()
        {
            Entry = new DirectoryEntry();
        }

        /// <summary>
        /// Initializes the service with a specified LDAP path.
        /// </summary>
        /// <param name="ldapPath">The LDAP path to use.</param>
        internal ADirectoryService(string ldapPath)
        {
            Entry = new DirectoryEntry(ldapPath);
        }
    }

    /// <summary>
    /// Provides methods for executing LDAP queries.
    /// </summary>
    internal sealed class LdapQueryService
    {
        private readonly ADirectoryService _directoryService;

        /// <summary>
        /// Initializes the LDAP query service with a given directory service.
        /// </summary>
        /// <param name="directoryService">The directory service to use for queries.</param>
        internal LdapQueryService(ADirectoryService directoryService)
        {
            _directoryService = directoryService;
        }

        /// <summary>
        /// Executes an LDAP query against the domain and retrieves the results.
        /// </summary>
        /// <param name="filter">The LDAP filter string to use for the query.</param>
        /// <param name="propertiesToLoad">The properties to include in the results.</param>
        /// <returns>A dictionary mapping entry paths to their properties and values.</returns>
        internal Dictionary<string, Dictionary<string, object[]>> ExecuteQuery(string filter, string[] propertiesToLoad = null)
        {
            using DirectorySearcher searcher = new(_directoryService.Entry)
            {
                Filter = filter
            };

            // Load specified properties if provided
            if (propertiesToLoad != null)
            {
                searcher.PropertiesToLoad.AddRange(propertiesToLoad);
            }

            SearchResultCollection results = searcher.FindAll();

            // Create a dictionary to store the results
            Dictionary<string, Dictionary<string, object[]>> resultDictionary = new();

            foreach (SearchResult result in results)
            {
                Dictionary<string, object[]> propertiesDictionary = new();

                foreach (DictionaryEntry property in result.Properties)
                {
                    var propertyValues = new List<object>();

                    foreach (object value in (ResultPropertyValueCollection)property.Value)
                    {
                        propertyValues.Add(value);
                    }

                    propertiesDictionary[property.Key.ToString()] = propertyValues.ToArray();
                }

                resultDictionary[result.Path] = propertiesDictionary;
            }

            return resultDictionary;
        }
    }
}
