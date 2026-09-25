using CRDTApp.Messages;
using Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Tests_1
{
    [TestClass]
    public class ActiveConnectionTrackerTests
    {
        [TestMethod]
        public void TestActiveConnectionTracker()
        {
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            
            var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID> { r1 }, new HashSet<ReplicaID> { r2 });
            
            Assert.IsTrue(tracker.IsActive(r1));
            Assert.IsFalse(tracker.IsActive(r2));
            
            Assert.AreEqual(1, tracker.ActiveConnections.Count);
            Assert.AreEqual(1, tracker.InnactiveConnections.Count);
            Assert.AreEqual(2, tracker.AllConnections.Count);
            
            // Set active
            var b1 = tracker.SetActive(r2);
            Assert.IsTrue(b1);
            Assert.IsTrue(tracker.IsActive(r2));
            
            var b2 = tracker.SetActive(r2);
            Assert.IsFalse(b2);
            
            // Set inactive
            var b3 = tracker.SetInnactive(r1);
            Assert.IsTrue(b3);
            Assert.IsFalse(tracker.IsActive(r1));
            
            var b4 = tracker.SetInnactive(r1);
            Assert.IsFalse(b4);
        }
    }
}

