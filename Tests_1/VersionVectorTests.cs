using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tests_1
{
    [TestClass]
    public sealed class VersionVectorTests
    {

        [TestMethod]
        public void BasicEqualityAndIncrement() {
            ReplicaID id1 = new ReplicaID(1);
            ReplicaID id2 = new ReplicaID(2);
            VersionVector vector1 = new VersionVector();
            VersionVector vector2 = new VersionVector();
            VersionVector vector3 = new VersionVector();

            vector1.Increment(id1);
            vector2.Increment(id2);
            vector3.Increment(id1);

            Assert.AreEqual(VersionVector.Comparison.Concurrent,vector1.Compare(vector2));
            Assert.AreEqual(VersionVector.Comparison.Equal, vector1.Compare(vector3));

            var v1 = vector1.CreateMerged(vector2);
            var v2 = vector2.CreateMerged(vector1);

            vector1.Merge(vector2);

            Assert.AreEqual(VersionVector.Comparison.Equal, v1.Compare(v2));
            Assert.AreEqual(VersionVector.Comparison.Equal, vector1.Compare(v1));
            Assert.AreEqual(VersionVector.Comparison.Equal, vector1.Compare(v2));


        }
    }
}
