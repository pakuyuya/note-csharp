using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace pakuyuya
{
    /// <summary>
    /// アプリケーション バックグラウンド キュー
    /// 
    /// Note: 
    /// Taskベースで直列実行の簡易なキューイングを実装したが
    /// ・Taskは有限のスレッドリソースをロックして実行している（1000個ぐらい作っても、並列実行できるのは先にリソース確保できたいくつかだけ）
    /// ・タスクをPushしたときにTaskを返さないがイケてない
    /// などの理由でお倉入りになった。
    /// Thredで実装しなおせば使えないことはないかもしれないが
    /// 導入しようとしているアプリは非常に簡素なUIなので、Taskで色々事足りると判断。
    /// </summary>
    public static class AppBGQueue
    {
        private static Dictionary<string, LinkedList<ProcessAction>> queues;
        private static Dictionary<string, ProcessTask> runningTask;
        private static int nextid = 0;
        private static object getIdCS = new object();
        private static object endequeueCS = new object();

        /// <summary>
        /// キューにタスクをpushします。
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="cancelable"></param>
        /// <param name="task"></param>
        /// <returns></returns>
        public static ProcessAction Push(string tag, bool cancelable, Action task)
        {
            var id = getNextId();
            var queue = new ProcessAction
            {
                queueid = id,
                cancelable = cancelable,
                task = task
            };

            lock (endequeueCS)
            {
                if (!queues.ContainsKey(tag))
                {
                    queues[tag] = new LinkedList<ProcessAction>();
                }

                queues[tag].AddLast(queue);
            }

            tryDispatch(tag);

            return queue;
        }

        /// <summary>
        /// 指定したタスクをキャンセルします。
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="id"></param>
        public static void Cancel(string tag, int id)
        {
            lock (endequeueCS)
            {
                if (!queues.ContainsKey(tag))
                {
                    var queue = queues[tag];
                    var target = new ProcessAction { queueid = -1 };
                    foreach (var taskInfo in queue)
                    {
                        if (taskInfo.queueid == id)
                        {
                            target = taskInfo;
                            break;
                        }
                    }
                    if (target.queueid != -1 && target.cancelable)
                    {
                        queue.Remove(target);
                    }
                }
                if (runningTask.ContainsKey(tag))
                {
                    var taskinfo = runningTask[tag];

                    if (taskinfo.queueid == id && taskinfo.cancelable)
                    {
                        runningTask[tag].cts.Cancel();
                    }
                }
            }
        }

        /// <summary>
        /// 指定したタスクの実行状態を確認します
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="id"></param>
        public static TaskStatus Status(string tag, int id)
        {
            if (queues.ContainsKey(tag))
            {
                var queue = queues[tag];
                foreach (var taskInfo in queue)
                {
                    if (taskInfo.queueid == id)
                    {
                        return TaskStatus.Wait;
                    }
                }
            }
            if (runningTask.ContainsKey(tag))
            {
                var taskinfo = runningTask[tag];

                if (taskinfo.queueid == id && !taskinfo.task.IsCompleted)
                {
                    return TaskStatus.Running;
                }
            }
            return TaskStatus.NoTaskOrDone;
        }

        /// <summary>
        /// 指定したタグに、実行待ちまたは実行中のタスクがあるか確認します。
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public static bool HasTask(string tag)
        {
            return (queues.ContainsKey(tag) && queues[tag].Count > 0)
                || (runningTask.ContainsKey(tag) && !runningTask[tag].task.IsCompleted);
        }

        private static int getNextId()
        {
            lock (getIdCS)
            {
                return nextid++;
            }
        }

        private static void tryDispatch(string tag)
        {
            lock (endequeueCS)
            {
                if (!queues.ContainsKey(tag) || queues[tag].Count == 0)
                {
                    return;
                }

                if (runningTask.ContainsKey(tag) && !runningTask[tag].task.IsCompleted)
                {
                    return;
                }

                var taskInfo = queues[tag].First.Value;
                queues[tag].RemoveFirst();

                var cts = new CancellationTokenSource();

                var newTask = new ProcessTask
                {
                    queueid = taskInfo.queueid,
                    cancelable = taskInfo.cancelable,
                    task = Task.Run(() => { taskInfo.task(); tryDispatch(tag); }, cts.Token),
                    cts = cts
                };

                runningTask[tag] = newTask;
            }
        }

        public struct ProcessAction
        {
            public int queueid;
            public bool cancelable;
            public Action task;
        }
        public struct ProcessTask
        {
            public int queueid;
            public bool cancelable;
            public Task task;
            public CancellationTokenSource cts;
        }
    }

    /// <summary>
    /// タスクの状態
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// 不明
        /// </summary>
        Unknown,        

        /// <summary>
        /// 実行待ち
        /// </summary>
        Wait,

        /// <summary>
        /// 実行中
        /// </summary>
        Running,

        /// <summary>
        /// 実行されていないまたは完了
        /// </summary>
        NoTaskOrDone
    }
}
