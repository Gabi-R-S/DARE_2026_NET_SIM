using Microsoft.VisualStudio.TestTools.UnitTesting;
using Simulator;
using Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimSharp;

namespace Tests_1
{
    [TestClass]
    public class SimulatorTests
    {
        [TestMethod]
        public void ConnectionMap_SetAndGetConnection()
        {
            var map = new ConnectionMap();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            var conn = new NetworkConnection { TransmissionTime = 1.0f, Active = true };

            map.SetConnection(r1, r2, conn);

            var retrieved = map.GetConnection(r1, r2);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(1.0f, retrieved.TransmissionTime);
            Assert.IsTrue(retrieved.Active);
            
            var retrieved2 = map.GetConnection(r2, r1);
            Assert.IsNotNull(retrieved2);
            Assert.AreEqual(retrieved, retrieved2);
        }

        [TestMethod]
        public void ConnectionMap_GetAllKnownReplicas()
        {
            var map = new ConnectionMap();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            var r3 = new ReplicaID(3);
            
            map.SetConnection(r1, r2, new NetworkConnection());
            map.SetConnection(r2, r3, new NetworkConnection());

            var replicas = map.GetAllKnownReplicas();
            Assert.AreEqual(3, replicas.Count);
            Assert.IsTrue(replicas.Contains(r1));
            Assert.IsTrue(replicas.Contains(r2));
            Assert.IsTrue(replicas.Contains(r3));
        }

        [TestMethod]
        public void ConnectionMap_IsConnectionAvailable()
        {
            var map = new ConnectionMap();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            var conn = new NetworkConnection { Active = true };
            map.SetConnection(r1, r2, conn);

            Assert.IsTrue(map.IsConnectionAvailable(r1, r2));
            Assert.IsFalse(map.IsConnectionAvailable(r1, new ReplicaID(3)));
            
            conn.Active = false;
            Assert.IsFalse(map.IsConnectionAvailable(r1, r2));
        }

        [TestMethod]
        public async Task RealtimeSimulator_ScheduleMessageDelivery()
        {
            var map = new ConnectionMap();
            var sim = new RealtimeSimulator(map);
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            map.SetConnection(r1, r2, new NetworkConnection { TransmissionTime = 0.5f, Active = true });

            var msg = new NetworkMessage(r1, r2, "test payload");

            bool messageDelivered = false;
            sim.AddMessageDeliveredHandler(r2, (m) => {
                if (m == msg) messageDelivered = true;
            });

            bool scheduled = sim.ScheduleMessageDelivery(msg);
            Assert.IsTrue(scheduled);

            await Task.Delay(1000);
            Assert.IsTrue(messageDelivered);
        }

        

        [TestMethod]
        public void DiscreteSimulator_ScheduleMessageDelivery()
        {
            var map = new ConnectionMap();
            var env = new Simulation();
            var sim = new DiscreteSimulator(env, map);
            
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            map.SetConnection(r1, r2, new NetworkConnection { TransmissionTime = 1.0f, Active = true });

            var msg = new NetworkMessage(r1, r2, "test payload");

            bool messageDelivered = false;
            sim.AddMessageDeliveredHandler(r2, (m) => {
                if (m == msg) messageDelivered = true;
            });

            bool scheduled = sim.ScheduleMessageDelivery(msg);
            Assert.IsTrue(scheduled);
            
            env.Run(TimeSpan.FromSeconds(2));
            Assert.IsTrue(messageDelivered);
        }
    }
}



