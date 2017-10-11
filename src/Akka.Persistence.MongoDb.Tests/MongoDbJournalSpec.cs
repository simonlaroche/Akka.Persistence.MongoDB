//-----------------------------------------------------------------------
// <copyright file="MongoDbJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Akka.Configuration;
using Microsoft.Extensions.Configuration;
using Akka.Persistence.TCK.Journal;
using Mongo2Go;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using Xunit;

namespace Akka.Persistence.MongoDb.Tests
{
    public class MongoDbRunnerFixture : IDisposable
    {
        private readonly IConfigurationRoot _config;
        private readonly MongoDbRunner _runner;

        public const string DefaultWindowsSearchPattern = @"tools\mongodb-win32*\bin";
        public const string DefaultLinuxSearchPattern = "tools/mongodb-linux*/bin";
        public const string DefaultOsxSearchPattern = "tools/mongodb-osx*/bin";

        public MongoDbRunnerFixture()
        {
            _config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddXmlFile("app.xml").Build();

            var databaseDirectory = _config.GetSection("appSettings").GetChildren().First().GetSection("value").Value;

            // Search in ~/.nuget/packages
            var searchPatternPrefix = Path.Combine(".nuget", "packages", "Mongo2Go*", GetOsSearchPattern());
            
            _runner = MongoDbRunner.Start(databaseDirectory, searchPatternPrefix);
        }

        private static string GetOsSearchPattern()
        {
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return DefaultOsxSearchPattern;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return DefaultLinuxSearchPattern;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return DefaultWindowsSearchPattern;
            }

            throw new MonogDbBinariesNotFoundException();
        }

        public string ConnectionString => _runner.ConnectionString;

        public void Dispose()
        {
            _runner?.Dispose();
        }
    }

    [CollectionDefinition("MongoDbSpec")]
    public class MongoDbSpecCollection : ICollectionFixture<MongoDbRunnerFixture>
    {
        // This class has no code, and is never created.Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }

    [Collection("MongoDbSpec")]
    public class MongoDbJournalSpec : JournalSpec
    {
        private readonly MongoDbRunnerFixture _mongoDbDbRunner;
        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        private static readonly string SpecConfig = @"
            akka.test.single-expect-default = 3s
            akka.persistence {
                publish-plugin-commands = on
                journal {
                    plugin = ""akka.persistence.journal.mongodb""
                    mongodb {
                        class = ""Akka.Persistence.MongoDb.Journal.MongoDbJournal, Akka.Persistence.MongoDb""
                        connection-string = ""<ConnectionString>""
                        auto-initialize = on
                        collection = ""EventJournal""
                    }
                }
            }";

        public MongoDbJournalSpec(MongoDbRunnerFixture mongoDbDbRunner) : base(CreateSpecConfig(mongoDbDbRunner.ConnectionString), "MongoDbJournalSpec")
        {
            _mongoDbDbRunner = mongoDbDbRunner;
            Initialize();
        }

        private static string CreateSpecConfig(string connectionString)
        {
            return SpecConfig.Replace("<ConnectionString>", connectionString + "akkanet");
        }

        protected override void Dispose(bool disposing)
        {
            new MongoClient(_mongoDbDbRunner.ConnectionString)
                .GetDatabase("akkanet")
                .DropCollectionAsync("EventJournal").Wait();
            
            base.Dispose(disposing);
        }
    }
}
