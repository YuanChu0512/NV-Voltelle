using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal static class VerifiedWriteTransaction
    {
        public static void Execute<TSnapshot>(
            bool writesEnabled,
            Func<TSnapshot> capture,
            Action<TSnapshot> validate,
            Action<TSnapshot> apply,
            Action<TSnapshot> verify)
        {
            if (!writesEnabled)
                throw new InvalidOperationException("硬件写入未启用。");
            if (capture == null) throw new ArgumentNullException("capture");
            if (validate == null) throw new ArgumentNullException("validate");
            if (apply == null) throw new ArgumentNullException("apply");
            if (verify == null) throw new ArgumentNullException("verify");

            TSnapshot before = capture();
            validate(before);
            apply(before);
            verify(before);
        }
    }

    internal sealed class WriteStepFailure
    {
        public string Label { get; set; }
        public string Message { get; set; }
    }

    internal sealed class BestEffortWriteResult
    {
        private readonly List<string> successfulSteps = new List<string>();
        private readonly List<WriteStepFailure> failedSteps = new List<WriteStepFailure>();

        public IList<string> SuccessfulSteps { get { return successfulSteps.AsReadOnly(); } }
        public IList<WriteStepFailure> FailedSteps { get { return failedSteps.AsReadOnly(); } }
        public bool HasFailures { get { return failedSteps.Count != 0; } }
        public bool HasSuccesses { get { return successfulSteps.Count != 0; } }

        public void Attempt(string label, Action action)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("写入步骤名称不能为空。", "label");
            if (action == null) throw new ArgumentNullException("action");
            try
            {
                action();
                successfulSteps.Add(label);
            }
            catch (Exception ex)
            {
                failedSteps.Add(new WriteStepFailure { Label = label, Message = Flatten(ex) });
            }
        }

        public void Merge(BestEffortWriteResult other, string prefix)
        {
            if (other == null) return;
            string actualPrefix = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + " ";
            for (int index = 0; index < other.successfulSteps.Count; index++)
                successfulSteps.Add(actualPrefix + other.successfulSteps[index]);
            for (int index = 0; index < other.failedSteps.Count; index++)
            {
                WriteStepFailure failure = other.failedSteps[index];
                failedSteps.Add(new WriteStepFailure
                {
                    Label = actualPrefix + failure.Label,
                    Message = failure.Message
                });
            }
        }

        public string SuccessfulLabels()
        {
            return successfulSteps.Count == 0 ? "无" : string.Join("、", successfulSteps.ToArray());
        }

        public string FailureDetails()
        {
            if (failedSteps.Count == 0) return "无";
            List<string> details = new List<string>();
            for (int index = 0; index < failedSteps.Count; index++)
                details.Add(failedSteps[index].Label + ": " + failedSteps[index].Message);
            return string.Join(" | ", details.ToArray());
        }

        private static string Flatten(Exception error)
        {
            AggregateException aggregate = error as AggregateException;
            if (aggregate == null) return error.Message;
            List<string> messages = new List<string>();
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                messages.Add(inner.Message);
            return string.Join(" / ", messages.ToArray());
        }
    }

    internal sealed class TuningRequest
    {
        public int CoreOffsetMHz { get; set; }
        public int MemoryOffsetMHz { get; set; }
        public int PowerPercent { get; set; }
        public bool BoostLockEnabled { get; set; }
    }

    internal sealed class TuningState
    {
        public int CoreOffsetMHz { get; set; }
        public int MemoryOffsetMHz { get; set; }
        public int PowerPercent { get; set; }
        public bool BoostLockEnabled { get; set; }
        public bool ExactRawValuesAvailable { get; set; }
        public int CoreOffsetKHz { get; set; }
        public int MemoryOffsetKHz { get; set; }
        public uint PowerRaw { get; set; }

        public bool Matches(TuningRequest request)
        {
            if (ExactRawValuesAvailable)
            {
                return CoreOffsetKHz == checked(request.CoreOffsetMHz * 1000) &&
                       MemoryOffsetKHz == checked(request.MemoryOffsetMHz * 1000) &&
                       PowerRaw == checked((uint)request.PowerPercent * 1000U) &&
                       BoostLockEnabled == request.BoostLockEnabled;
            }
            return CoreOffsetMHz == request.CoreOffsetMHz &&
                   MemoryOffsetMHz == request.MemoryOffsetMHz &&
                   PowerPercent == request.PowerPercent &&
                   BoostLockEnabled == request.BoostLockEnabled;
        }
    }

    internal interface ITuningWriter
    {
        void ApplyCoreOffsetVerified(int offsetMHz);
        void ApplyMemoryOffsetVerified(int offsetMHz);
        void ApplyPowerLimitVerified(int percentage);
        void ApplyBoostLockVerified(bool enabled);
    }

    internal sealed class SafeWriteCoordinator
    {
        private readonly ITuningWriter writer;
        private readonly bool writesEnabled;

        public SafeWriteCoordinator(ITuningWriter writer, bool writesEnabled)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            this.writer = writer;
            this.writesEnabled = writesEnabled;
        }

        public BestEffortWriteResult ApplyVerified(TuningRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (!writesEnabled) throw new InvalidOperationException("硬件写入未启用。");

            BestEffortWriteResult result = new BestEffortWriteResult();
            result.Attempt("核心频率偏移", delegate { writer.ApplyCoreOffsetVerified(request.CoreOffsetMHz); });
            result.Attempt("显存频率偏移", delegate { writer.ApplyMemoryOffsetVerified(request.MemoryOffsetMHz); });
            result.Attempt("功耗上限", delegate { writer.ApplyPowerLimitVerified(request.PowerPercent); });
            result.Attempt("Boost Lock", delegate { writer.ApplyBoostLockVerified(request.BoostLockEnabled); });
            return result;
        }
    }
}
