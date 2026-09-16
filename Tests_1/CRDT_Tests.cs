using CRDTs;
using CRDTs.Basic;
using Shared;

namespace Tests_1
{
    [TestClass]
    public sealed class CRDT_Tests
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
            Assert.AreEqual(1, counter1.Value);

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

        [TestMethod]
        public void TestPNCounter()
        {
            var counter1 = new PNCounter();
            var counter2 = new PNCounter();
            
            var replica1 = new ReplicaID(1);
            var replica2 = new ReplicaID(2);
            
            var inc1 = counter1.CreateIncrementOperation(replica1);
            counter1.Merge(inc1);

            Assert.AreEqual(1,counter1.Value);


            var dec2 = counter2.CreateDecrementOperation(replica2);
            counter2.Merge(dec2);

            Assert.AreEqual(-1, counter2.Value);

            counter2.Merge(inc1);
            counter1.Merge(dec2);

            Assert.AreEqual(0,counter1.Value);
            Assert.AreEqual(0, counter2.Value);
        }
        [TestMethod]
        public void TestLWWRegister() {
            var now = DateTime.Now;
            var register1 = new LWWRegister<int>(now,0);
            var register2 = new LWWRegister<int>(now, 0);

            var replica1 = new ReplicaID(1);
            var replica2 = new ReplicaID(2);

            var w1 = register1.CreateWriteOperation(DateTime.Now,10);
            register1.Merge(w1);

            var w2 = register2.CreateWriteOperation(DateTime.Now,15);
            register2.Merge(w2);

            Assert.AreEqual(10,register1.Value);
            Assert.AreEqual(15, register2.Value);

            register1.Merge(w2);
            register2.Merge(w1);
            Assert.AreEqual(15, register1.Value);
            Assert.AreEqual(15, register2.Value);
        }
    }
}
