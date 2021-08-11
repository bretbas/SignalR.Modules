﻿using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SignalR.Modules
{
    public class ModuleHubTypedClients<TModuleHubClient> : IHubClients<TModuleHubClient>
        where TModuleHubClient : class
    {
        private readonly IHubClients<IClientProxy> hubCallerClients;

        public ModuleHubTypedClients(IHubClients<IClientProxy> hubContext)
        {
            hubCallerClients = hubContext;
        }

        public TModuleHubClient All => CreateClient(hubCallerClients.All);

        public TModuleHubClient AllExcept(IReadOnlyList<string> excludedConnectionIds)
        {
            return CreateClient(hubCallerClients.AllExcept(excludedConnectionIds));
        }

        public TModuleHubClient Client(string connectionId)
        {
            return CreateClient(hubCallerClients.Client(connectionId));
        }

        public TModuleHubClient Group(string groupName)
        {
            return CreateClient(hubCallerClients.Group(groupName));
        }

        public TModuleHubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds)
        {
            return CreateClient(hubCallerClients.GroupExcept(groupName, excludedConnectionIds));
        }

        public TModuleHubClient Groups(IReadOnlyList<string> groupNames)
        {
            return CreateClient(hubCallerClients.Groups(groupNames));
        }

        public TModuleHubClient User(string userId)
        {
            return CreateClient(hubCallerClients.Users(userId));
        }

        public TModuleHubClient Users(IReadOnlyList<string> userIds)
        {
            return CreateClient(hubCallerClients.Users(userIds));
        }

        TModuleHubClient IHubClients<TModuleHubClient>.Clients(IReadOnlyList<string> connectionIds)
        {
            return CreateClient(hubCallerClients.Clients(connectionIds));
        }

        protected virtual TModuleHubClient CreateClient(IClientProxy clientProxy)
        {
            // todo: improve this. maybe a solution without reflection?
            var interfaceType = typeof(TModuleHubClient);

            var implType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => GetLoadableTypes(a))
                .Single(t => interfaceType.IsAssignableFrom(t) && !t.IsAbstract);
            return Activator.CreateInstance(implType, clientProxy) as TModuleHubClient;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }
    }
}