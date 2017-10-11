//-----------------------------------------------------------------------
// <copyright file="MongoDbSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Mongo2Go;
using MongoDB.Driver;
using Xunit;
using Akka.Persistence.TCK.Snapshot;

namespace Akka.Persistence.MongoDb.Tests
{
    [Collection("MongoDbSpec")]
    public class MongoDbSnapshotStoreSpec : SnapshotStoreSpec
    {
        private readonly MongoDbRunnerFixture _mongoDbRunnerFixture;

        private static readonly string SpecConfig = @"
            akka.test.single-expect-default = 3s
            akka.persistence {
                publish-plugin-commands = on
                snapshot-store {
                    plugin = ""akka.persistence.snapshot-store.mongodb""
                    mongodb {
                        class = ""Akka.Persistence.MongoDb.Snapshot.MongoDbSnapshotStore, Akka.Persistence.MongoDb""
                        connection-string = ""<ConnectionString>""
                        auto-initialize = on
                        collection = ""SnapshotStore""
                    }
                }
            }";

        public MongoDbSnapshotStoreSpec(MongoDbRunnerFixture mongoDbRunnerFixture) : base(CreateSpecConfig(mongoDbRunnerFixture.ConnectionString), "MongoDbSnapshotStoreSpec")
        {
            _mongoDbRunnerFixture = mongoDbRunnerFixture;
            Initialize();
        }
        
        private static string CreateSpecConfig(string connectionString)
        {
            return SpecConfig.Replace("<ConnectionString>", connectionString + "akkanet");
        }

        protected override void Dispose(bool disposing)
        {
            new MongoClient(_mongoDbRunnerFixture.ConnectionString)
                .GetDatabase("akkanet")
                .DropCollectionAsync("SnapshotStore").Wait();

            base.Dispose(disposing);
        }
    }
}
