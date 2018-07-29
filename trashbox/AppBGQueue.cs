using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace pakuyuya
{
    /// <summary>
    /// �A�v���P�[�V���� �o�b�N�O���E���h �L���[
    /// 
    /// Note: 
    /// Task�x�[�X�Œ�����s�̊ȈՂȃL���[�C���O������������
    /// �ETask�͗L���̃X���b�h���\�[�X�����b�N���Ď��s���Ă���i1000���炢����Ă��A������s�ł���̂͐�Ƀ��\�[�X�m�ۂł��������������j
    /// �E�^�X�N��Push�����Ƃ���Task��Ԃ��Ȃ����C�P�ĂȂ�
    /// �Ȃǂ̗��R�ł��q����ɂȂ����B
    /// Thred�Ŏ������Ȃ����Ύg���Ȃ����Ƃ͂Ȃ���������Ȃ���
    /// �������悤�Ƃ��Ă���A�v���͔��Ɋȑf��UI�Ȃ̂ŁATask�ŐF�X�������Ɣ��f�B
    /// </summary>
    public static class AppBGQueue
    {
        private static Dictionary<string, LinkedList<ProcessAction>> queues;
        private static Dictionary<string, ProcessTask> runningTask;
        private static int nextid = 0;
        private static object getIdCS = new object();
        private static object endequeueCS = new object();

        /// <summary>
        /// �L���[�Ƀ^�X�N��push���܂��B
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
        /// �w�肵���^�X�N���L�����Z�����܂��B
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
        /// �w�肵���^�X�N�̎��s��Ԃ��m�F���܂�
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
        /// �w�肵���^�O�ɁA���s�҂��܂��͎��s���̃^�X�N�����邩�m�F���܂��B
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
    /// �^�X�N�̏��
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// �s��
        /// </summary>
        Unknown,        

        /// <summary>
        /// ���s�҂�
        /// </summary>
        Wait,

        /// <summary>
        /// ���s��
        /// </summary>
        Running,

        /// <summary>
        /// ���s����Ă��Ȃ��܂��͊���
        /// </summary>
        NoTaskOrDone
    }
}
