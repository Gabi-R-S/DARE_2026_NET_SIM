using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;

namespace Tests_1
{
    public class ControllableTestScheduler : IScheduler
    {
        private readonly List<(DateTime scheduledTime, Action action)> _scheduledActions = new();

        public DateTime CurrentTime { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public Func<Task, float, Task<bool>>? TimeoutOverride { get; set; }

        public bool ForceTimeout { get; set; } = false;

        public DateTime GetCurrentTime() => CurrentTime;

        public void ExecuteAfter(float time, Action action)
        {
            var triggerTime = CurrentTime.AddSeconds(time);
            _scheduledActions.Add((triggerTime, action));
        }

        public async Task<bool> ExecuteTaskOrTimeout(Task task, float time)
        {
            if (TimeoutOverride != null)
            {
                return await TimeoutOverride(task, time);
            }

            if (ForceTimeout)
            {
                return false;
            }

            if (task.IsCompleted)
            {
                return true;
            }

            // If not completed, either wait briefly or evaluate
            var delayMs = (int)(time * 1000);
            if (delayMs <= 0) delayMs = 1;
            if (delayMs > 500) delayMs = 500;

            var delayTask = Task.Delay(delayMs);
            var completed = await Task.WhenAny(task, delayTask);
            return completed == task;
        }

        public async Task<(bool, T)> ExecuteTaskOrTimeout<T>(Task<T> task, float time, T defaultValue)
        {
            if (ForceTimeout)
            {
                return (false, defaultValue);
            }

            if (task.IsCompleted)
            {
                return (true, await task);
            }

            var delayMs = (int)(time * 1000);
            if (delayMs <= 0) delayMs = 1;
            if (delayMs > 500) delayMs = 500;

            var delayTask = Task.Delay(delayMs);
            var completed = await Task.WhenAny(task, delayTask);
            if (completed == task)
            {
                return (true, await task);
            }

            return (false, defaultValue);
        }

        public void AdvanceTime(TimeSpan delta)
        {
            CurrentTime += delta;
            TriggerPendingActions();
        }

        public void AdvanceSeconds(float seconds)
        {
            AdvanceTime(TimeSpan.FromSeconds(seconds));
        }

        public void TriggerPendingActions()
        {
            var dueActions = _scheduledActions
                .Where(x => x.scheduledTime <= CurrentTime)
                .OrderBy(x => x.scheduledTime)
                .ToList();

            foreach (var due in dueActions)
            {
                _scheduledActions.Remove(due);
            }

            foreach (var due in dueActions)
            {
                due.action();
            }
        }

        public int PendingActionsCount => _scheduledActions.Count;
    }

    public class ControllableMockLayer : NetworkLayer
    {
        public List<(ReplicaID Recipient, Message Message)> SentMessages { get; } = new();
        public Func<ReplicaID, Message, Task<bool>>? OnSendAsync { get; set; }

        public ControllableMockLayer(NetworkLayer? layerBelow = null, bool connectMessageReceived = false, bool connectConnectionInterrupted = false, bool connectConnectionRegained = false)
            : base(layerBelow, connectMessageReceived, connectConnectionInterrupted, connectConnectionRegained)
        {
        }

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            SentMessages.Add((recipient, message));
            if (OnSendAsync != null)
            {
                return await OnSendAsync(recipient, message);
            }
            return true;
        }

        public void InjectReceivedMessage(ReplicaID sender, Message message)
        {
            CallMessageReceived(sender, message);
        }

        public void InjectConnectionInterrupted(ReplicaID replica)
        {
            CallConnectionInterrupted(replica);
        }

        public void InjectConnectionRegained(ReplicaID replica)
        {
            CallConnectionRegained(replica);
        }
    }

    public static class TestNetworkLayerExtensions
    {
        public static void SetRunning(this NetworkLayer layer, bool running = true)
        {
            var prop = typeof(NetworkLayer).GetProperty("Running", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            prop?.SetValue(layer, running);
        }
    }
}
