using CRDTs.Basic;
using Shared;

namespace Tests_1
{
    [TestClass]
    public sealed class Test1
    {
        [TestMethod]
        public void TestGCounter()
        {
            GCounter counter1 = new GCounter();
            GCounter counter2 = new GCounter();
            GCounter counter3 = new GCounter();
            var replica1 = new ReplicaID(1);
            var replica2 = new ReplicaID(2);
            var replica3 = new ReplicaID(3);

            var op1 = counter1.CreateAddOperation(replica1);
            counter1.Merge(op1);
            Assert.AreEqual(1,counter1.Value);

            var op2 = counter2.CreateAddOperation(replica2);
            counter2.Merge(op2);
            Assert.AreEqual(1, counter2.Value);

            counter2.Merge(op1);
            counter1.Merge(op2);
            Assert.AreEqual(2, counter1.Value);
            Assert.AreEqual(2, counter2.Value);


            counter3.Merge(op1);
            counter3.Merge(op2);
            var op3 = counter3.CreateAddOperation(replica3);

            counter1.Merge(op3);
            counter2.Merge(op3);
            counter3.Merge(op3);

            Assert.AreEqual(3, counter1.Value);
            Assert.AreEqual(3, counter2.Value);
            Assert.AreEqual(3, counter3.Value);
        }
    }
}
