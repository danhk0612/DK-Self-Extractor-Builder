using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DKSelfExtractor
{
    // Native Windows file dialog: FOS_PICKFOLDERS + FOS_ALLOWMULTISELECT.
    // No third-party UI dependency; allows Shift/Ctrl selection of several folders.
    internal static class MultiFolderPicker
    {
        internal static string[] Choose(IntPtr owner)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();
            IShellItemArray selected = null;
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | 0x20u | 0x40u | 0x200u);
                dialog.SetTitle("폴더 추가 (Ctrl/Shift로 여러 폴더 선택)");
                int hr = dialog.Show(owner);
                if (hr == unchecked((int)0x800704C7)) return new string[0];
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                dialog.GetResults(out selected);
                uint count;
                selected.GetCount(out count);
                var paths = new List<string>();
                for (uint index = 0; index < count; index++)
                {
                    IShellItem item;
                    selected.GetItemAt(index, out item);
                    try
                    {
                        IntPtr pointer;
                        item.GetDisplayName(0x80058000u, out pointer); // SIGDN_FILESYSPATH
                        try { paths.Add(Marshal.PtrToStringUni(pointer)); }
                        finally { Marshal.FreeCoTaskMem(pointer); }
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
                return paths.ToArray();
            }
            finally
            {
                if (selected != null) Marshal.ReleaseComObject(selected);
                Marshal.ReleaseComObject(dialog);
            }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specifications);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IntPtr item);
            void SetFolder(IntPtr item);
            void GetFolder(out IntPtr item);
            void GetCurrentSelection(out IntPtr item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IntPtr item);
            void AddPlace(IntPtr item, uint placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid riid, out IntPtr value);
            void GetPropertyStore(uint flags, ref Guid riid, out IntPtr store);
            void GetPropertyDescriptionList(IntPtr key, ref Guid riid, out IntPtr list);
            void GetAttributes(uint flags, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid riid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind, out IntPtr displayName);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
