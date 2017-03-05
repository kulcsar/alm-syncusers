using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SyncTfsUsers
{
    class Program
    {
        /// <summary>
        /// Get valid users from source TFS server and add them into target TFS server's group or team. 
        /// Sample command line arguments: http://tfs2015:8080/tfs http://tfs2015:8080/tfs/CollectionA "Agile TFVC" WTOTemp
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            if (args.Length == 1 || args.Length == 4)
            {
                string sourceTfsServerUrl = args[0]; //"http://tfs2015:8080/tfs"

                Console.WriteLine($"Getting Project Collection Valid Users members from [{sourceTfsServerUrl}] each collection's...");
                Console.WriteLine("Result [DisplayName;AccountName]:");
                Console.WriteLine();

                List<User> allTfsValidUsers = GetAllTfsValidUsers(sourceTfsServerUrl);
                if (allTfsValidUsers.Any())
                {
                    foreach (var user in allTfsValidUsers)
                    {
                        Console.WriteLine($"[{user.DisplayName};{user.AccountName}]");
                    }
                }
                else
                {
                    Console.WriteLine("User not found.");
                    Console.WriteLine();
                    Console.WriteLine("Press enter key to finish.");
                    Console.ReadLine();
                    return;
                }

                Console.WriteLine();
                Console.WriteLine("Press enter key to continue...");
                Console.ReadLine();

                if (args.Length == 4)
                {
                    string targetTeamProjectCollectionUrl = args[1]; // "http://tfs2015:8080/tfs/CollectionA"
                    string targetTeamProject = args[2]; // "Agile TFVC"
                    string targetGroupOrTeam = args[3]; // "WTOTemp"

                    Console.WriteLine($"Adding users to [{targetTeamProject}]\\{targetGroupOrTeam}.");
                    Console.WriteLine();

                    AddUsersToGroupOrTeam(targetTeamProjectCollectionUrl, targetTeamProject, targetGroupOrTeam, allTfsValidUsers);

                    Console.WriteLine();
                    Console.WriteLine("Adding users completed.");
                    Console.WriteLine();
                }

                Console.WriteLine("Press enter key to finish.");
                Console.ReadLine();
            }
            else
            {
                Console.WriteLine("TFS User synchronization tool.\n Usage: SyncUsers <source TFS URL> <target Team Project Collection Url> <target Team Project's name> <target Group or Team's name>\n");
            }
        }

        private static TfsConfigurationServer Connect(string tfsServerUrl)
        {
            // Set the URI of the Team Foundation Server
            Uri tfsServerUri = new Uri(tfsServerUrl);

            // Get a TfsConfigurationServer instance
            TfsConfigurationServer configServer = TfsConfigurationServerFactory.GetConfigurationServer(tfsServerUri);
            configServer.EnsureAuthenticated();

            return configServer;
        }

        private static List<User> GetAllTfsValidUsers(string tfsServerUrl)
        {
            List<User> tfsUsers = new List<User>();

            TfsConfigurationServer configServer = Connect(tfsServerUrl);

            // Get all Team Project Collections
            ITeamProjectCollectionService tpcService = configServer.GetService<ITeamProjectCollectionService>();
            foreach (TeamProjectCollection tpc in tpcService.GetCollections())
            {
                TfsTeamProjectCollection teamProjectCollection = configServer.GetTeamProjectCollection(tpc.Id);
                teamProjectCollection.EnsureAuthenticated();

                var identityManagementService = teamProjectCollection.GetService<IIdentityManagementService>();
                // Get members of Project Collection Valid Users group
                var collectionWideValidUsers = identityManagementService.ReadIdentity(IdentitySearchFactor.DisplayName,
                                                                                      "Project Collection Valid Users",
                                                                                      MembershipQuery.Expanded,
                                                                                      ReadIdentityOptions.None);
                var validMembers = identityManagementService.ReadIdentities(collectionWideValidUsers.Members,
                                                                            MembershipQuery.Expanded,
                                                                            ReadIdentityOptions.ExtendedProperties);
                var members = validMembers
                          .Where(m => !m.IsContainer
                                        && m.Descriptor.IdentityType != "Microsoft.TeamFoundation.UnauthenticatedIdentity"
                                        && m.Descriptor.IdentityType != "Microsoft.TeamFoundation.ServiceIdentity")
                          .Select(m => new User { DisplayName = m.DisplayName, AccountName = m.UniqueName })
                          .ToList();

                // Collect members
                foreach (var m in members)
                {
                    if (!tfsUsers.Any(t => t.AccountName == m.AccountName))
                        tfsUsers.Add(m);
                }
            }

            tfsUsers = tfsUsers.OrderBy(m => m.DisplayName).ToList();

            return tfsUsers;
        }

        private static void AddUsersToGroupOrTeam(string targetTeamProjectCollectionUrl, string targetTeamProject, string targetGroupOrTeam, List<User> usersToAdd)
        {
            var tpc = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(targetTeamProjectCollectionUrl));
            tpc.EnsureAuthenticated();

            var ims = tpc.GetService<IIdentityManagementService>();

            var targetTeamName = $"[{targetTeamProject}]\\{targetGroupOrTeam}";
            var tfsGroupIdentity = ims.ReadIdentity(IdentitySearchFactor.AccountName,
                                                    targetTeamName,
                                                    MembershipQuery.None,
                                                    ReadIdentityOptions.IncludeReadFromSource);
            if (tfsGroupIdentity != null)
            {
                foreach (var user in usersToAdd)
                {
                    var userIdentity = ims.ReadIdentity(IdentitySearchFactor.AccountName,
                                                            user.AccountName,
                                                            MembershipQuery.None,
                                                            ReadIdentityOptions.IncludeReadFromSource);
                    if (userIdentity != null)
                    {
                        try
                        {
                            ims.AddMemberToApplicationGroup(tfsGroupIdentity.Descriptor, userIdentity.Descriptor);
                            Console.WriteLine($"{user.AccountName} added.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{user.AccountName} not added: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{user.AccountName} not found.");
                    }
                }
            }
            else
            {
                Console.WriteLine($"{targetTeamName} not found.");
            }
        }
    }

    class User
    {
        public string DisplayName { get; set; }
        public string AccountName { get; set; }
    }
}
