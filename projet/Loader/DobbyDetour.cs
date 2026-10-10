using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace EllyrLoader
{
    // Bibliothèque native dobby.dll (fournie avec BepInEx, licence Apache-2.0) : pose des détours sur du code natif.
    internal static unsafe class DobbyLib
    {
        [DllImport("dobby", EntryPoint = "DobbyPrepare", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Prepare(nint target, nint replacement, nint* originalCall);

        [DllImport("dobby", EntryPoint = "DobbyCommit", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Commit(nint target);

        [DllImport("dobby", EntryPoint = "DobbyDestroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Destroy(nint target);

        public static void InstallResolver(string libFolder)
        {
            NativeLibrary.SetDllImportResolver(typeof(DobbyLib).Assembly, delegate (string name, Assembly assembly, DllImportSearchPath? search)
            {
                if (name != "dobby")
                    return IntPtr.Zero;
                return NativeLibrary.Load(Path.Combine(libFolder, "dobby.dll"));
            });
        }
    }

    // Un détour natif posé par dobby, au format attendu par Il2CppInterop.
    internal sealed unsafe class DobbyDetour : IDetour
    {
        private readonly nint _target;
        private readonly Delegate _replacement;
        private readonly nint _replacementPointer;
        private readonly List<object> _keepAlive = new List<object>();
        private nint _trampoline;
        private bool _prepared;
        private bool _applied;

        public DobbyDetour(nint original, Delegate replacement)
        {
            _target = FollowExportThunks(original);
            _replacement = replacement;
            _replacementPointer = Marshal.GetFunctionPointerForDelegate(replacement);
            _keepAlive.Add(replacement);
        }

        public nint Target { get { return _target; } }
        public nint Detour { get { return _replacementPointer; } }
        public nint OriginalTrampoline { get { return _trampoline; } }

        private void Prepare()
        {
            if (_prepared)
                return;

            nint trampoline = 0;
            DobbyLib.Prepare(_target, _replacementPointer, &trampoline);
            _trampoline = trampoline;
            _prepared = true;
        }

        public void Apply()
        {
            if (_applied)
                return;

            Prepare();
            DobbyLib.Commit(_target);
            _applied = true;
        }

        public T GenerateTrampoline<T>() where T : Delegate
        {
            Prepare();
            T trampoline = Marshal.GetDelegateForFunctionPointer<T>(_trampoline);
            _keepAlive.Add(trampoline);
            return trampoline;
        }

        public void Dispose()
        {
            if (_applied)
                DobbyLib.Destroy(_target);
            _applied = false;
            _keepAlive.Clear();
        }

        // Les exports d'une DLL sont souvent un petit saut vers la vraie fonction : dobby refuse de le patcher.
        // On suit ces sauts jusqu'au début de la fonction.
        private static nint FollowExportThunks(nint function)
        {
            if (function == 0)
                return function;

            nint current = function;
            for (int hops = 0; hops < 8; hops++)
            {
                byte opcode;
                try { opcode = Marshal.ReadByte(current); }
                catch (Exception) { return current; }

                if (opcode == 0xE9)
                {
                    current += 5 + Marshal.ReadInt32(current + 1);
                    continue;
                }

                if (opcode == 0xFF && Marshal.ReadByte(current + 1) == 0x25)
                {
                    if (IntPtr.Size == 8)
                        current = Marshal.ReadIntPtr(current + 6 + Marshal.ReadInt32(current + 2));
                    else
                        current = Marshal.ReadIntPtr((nint)(uint)Marshal.ReadInt32(current + 2));
                    continue;
                }

                break;
            }
            return current;
        }
    }

    internal sealed class DobbyDetourProvider : IDetourProvider
    {
        public IDetour Create<TDelegate>(nint original, TDelegate target) where TDelegate : Delegate
        {
            return new DobbyDetour(original, target);
        }
    }
}
