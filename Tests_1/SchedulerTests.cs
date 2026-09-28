using Microsoft.VisualStudio.TestTools.UnitTesting;
using CRDTApp.Schedulers;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using Simulator;
using Shared;

namespace Tests_1
{
    [TestClass]
    public class SchedulerTests
    {
        [TestMethod]
        public void RealtimeScheduler_GetCurrentTime_ReturnsCurrentTime()
        {
            var scheduler = new RealtimeScheduler();
            var time = scheduler.GetCurrentTime();
            var now = DateTime.Now;
            Assert.IsTrue((now - time).TotalSeconds < 1);
        }

        [TestMethod]
        public async Task RealtimeScheduler_ExecuteTaskOrTimeout_Success()
        {
            var scheduler = new RealtimeScheduler();
            var task = Task.Run(async () => {
                await Task.Delay(100);
                return 42;
            });
            var (success, result) = await scheduler.ExecuteTaskOrTimeout(task, 2.0f, 0);
            Assert.IsTrue(success);
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public async Task RealtimeScheduler_ExecuteTaskOrTimeout_Timeout()
        {
            var scheduler = new RealtimeScheduler();
            var task = Task.Run(async () => {
                await Task.Delay(2000);
                return 42;
            });
            var (success, result) = await scheduler.ExecuteTaskOrTimeout(task, 0.5f, 0);
            Assert.IsFalse(success);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void RealtimeScheduler_ExecuteAfter_Executes()
        {
            var scheduler = new RealtimeScheduler();
            bool executed = false;
            var tcs = new TaskCompletionSource<bool>();
            
            scheduler.ExecuteAfter(0.5f, () => {
                executed = true;
                tcs.SetResult(true);
            });
            
            var result = tcs.Task.Wait(2000);
            Assert.IsTrue(result);
            Assert.IsTrue(executed);
        }

        [TestMethod]
        public async Task SimulationScheduler_ExecuteTaskOrTimeout_Success()
        {
            var map = new ConnectionMap();
            var sim = new RealtimeSimulator(map);
            var scheduler = new SimulationScheduler(sim);
            
            var task = Task.Run(async () => {
                await Task.Delay(100);
                return 42;
            });
            
            var (success, result) = await scheduler.ExecuteTaskOrTimeout(task, 2.0f, 0);
            Assert.IsTrue(success);
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public async Task SimulationScheduler_ExecuteTaskOrTimeout_Timeout()
        {
            var map = new ConnectionMap();
            var sim = new RealtimeSimulator(map);
            var scheduler = new SimulationScheduler(sim);
            
            var task = Task.Run(async () => {
                await Task.Delay(2000);
                return 42;
            });
            
            var (success, result) = await scheduler.ExecuteTaskOrTimeout(task, 0.5f, 0);
            Assert.IsFalse(success);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void SimulationScheduler_ExecuteAfter_Executes()
        {
            var map = new ConnectionMap();
            var sim = new RealtimeSimulator(map);
            var scheduler = new SimulationScheduler(sim);
            
            bool executed = false;
            var tcs = new TaskCompletionSource<bool>();
            
            scheduler.ExecuteAfter(0.5f, () => {
                executed = true;
                tcs.SetResult(true);
            });
            
            var result = tcs.Task.Wait(2000);
            Assert.IsTrue(result);
            Assert.IsTrue(executed);
        }
    }
}
