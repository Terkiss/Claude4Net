using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Claude4Net.Tests
{
    internal sealed class ProcessTreeJob : IDisposable
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private SafeFileHandle? _jobHandle;

        private ProcessTreeJob(SafeFileHandle? jobHandle)
        {
            _jobHandle = jobHandle;
        }

        internal static ProcessTreeJob Attach(Process process)
        {
            if (!OperatingSystem.IsWindows())
            {
                return new ProcessTreeJob(null);
            }

            SafeFileHandle jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (jobHandle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };
            uint limitsSize = checked((uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>());
            if (!SetInformationJobObject(jobHandle, 9, ref limits, limitsSize) ||
                !AssignProcessToJobObject(jobHandle, process.Handle))
            {
                int error = Marshal.GetLastWin32Error();
                jobHandle.Dispose();
                throw new Win32Exception(error);
            }

            return new ProcessTreeJob(jobHandle);
        }

        internal static ProcessTreeJob StartContained(
            Process process,
            bool releaseGate = true,
            Func<Process, ProcessTreeJob>? attach = null)
        {
            ProcessTreeJob? processTree = null;
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("SDK process did not start.");
                }

                processTree = (attach ?? Attach)(process);
                if (releaseGate)
                {
                    processTree.ReleaseGate(process);
                }

                return processTree;
            }
            catch
            {
                CloseFailedStart(process, processTree);
                throw;
            }
        }

        internal void ReleaseGate(Process process)
        {
            try
            {
                process.StandardInput.BaseStream.WriteByte(1);
                process.StandardInput.BaseStream.Flush();
                process.StandardInput.Close();
            }
            catch
            {
                CloseFailedStart(process, this);
                throw;
            }
        }

        internal bool Contains(Process process)
        {
            if (_jobHandle is null)
            {
                return false;
            }

            if (!IsProcessInJob(process.Handle, _jobHandle, out bool isInJob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return isInJob;
        }

        internal void Kill(Process process)
        {
            if (_jobHandle is not null)
            {
                _jobHandle.Dispose();
                _jobHandle = null;
                return;
            }

            process.Kill(entireProcessTree: true);
        }

        public void Dispose()
        {
            _jobHandle?.Dispose();
            _jobHandle = null;
        }

        private static void CloseFailedStart(Process process, ProcessTreeJob? processTree)
        {
            try
            {
                if (!process.HasExited)
                {
                    if (processTree is null)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        processTree.Kill(process);
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                processTree?.Dispose();
                process.Dispose();
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            SafeFileHandle jobHandle,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle jobHandle, IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsProcessInJob(
            IntPtr processHandle,
            SafeFileHandle? jobHandle,
            [MarshalAs(UnmanagedType.Bool)] out bool result);

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            internal long PerProcessUserTimeLimit;
            internal long PerJobUserTimeLimit;
            internal uint LimitFlags;
            internal UIntPtr MinimumWorkingSetSize;
            internal UIntPtr MaximumWorkingSetSize;
            internal uint ActiveProcessLimit;
            internal UIntPtr Affinity;
            internal uint PriorityClass;
            internal uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            internal ulong ReadOperationCount;
            internal ulong WriteOperationCount;
            internal ulong OtherOperationCount;
            internal ulong ReadTransferCount;
            internal ulong WriteTransferCount;
            internal ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            internal JobObjectBasicLimitInformation BasicLimitInformation;
            internal IoCounters IoInfo;
            internal UIntPtr ProcessMemoryLimit;
            internal UIntPtr JobMemoryLimit;
            internal UIntPtr PeakProcessMemoryUsed;
            internal UIntPtr PeakJobMemoryUsed;
        }
    }
}
