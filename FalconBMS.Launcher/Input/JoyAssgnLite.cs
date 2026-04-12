using System;
using System.Globalization;
using System.Text;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// In-memory model for joystick DX button and POV hat assignments, including read/write helpers for keymapping behavior.
/// </summary>

public sealed class JoyAssgnLite
{
    public const string F15ProfileTag = "F15ABCD";

    public string ProductName { get; }

    public DxButton[] Dx { get; private set; }
    public PovHat[] Pov { get; private set; }

    private DxButton[] _profileDefaultF16Dx;
    private PovHat[] _profileDefaultF16Pov;

    private DxButton[] _profileF15Dx;
    private PovHat[] _profileF15Pov;

    public string? CurrentAvionicsProfile { get; private set; } = null;

    public JoyAssgnLite(
        string productName,
        DxButton[] dxActive,
        PovHat[] povActive,
        DxButton[]? profileDefaultF16Dx = null,
        PovHat[]? profileDefaultF16Pov = null,
        DxButton[]? profileF15Dx = null,
        PovHat[]? profileF15Pov = null)
    {
        ProductName = productName;

        Dx = dxActive;
        Pov = povActive;

        _profileDefaultF16Dx = profileDefaultF16Dx ?? dxActive;
        _profileDefaultF16Pov = profileDefaultF16Pov ?? povActive;

        _profileF15Dx = profileF15Dx ?? dxActive;
        _profileF15Pov = profileF15Pov ?? povActive;
    }

    public void SelectAvionicsProfile(string? avionicsProfile)
    {
        bool targetIsF15 = IsF15(avionicsProfile);
        bool currentIsF15 = IsF15(CurrentAvionicsProfile);

        if (targetIsF15 == currentIsF15)
        {
            CurrentAvionicsProfile = avionicsProfile;
            return;
        }

        if (currentIsF15)
        {
            _profileF15Dx = Dx;
            _profileF15Pov = Pov;
        }
        else
        {
            _profileDefaultF16Dx = Dx;
            _profileDefaultF16Pov = Pov;
        }

        if (targetIsF15)
        {
            Dx = _profileF15Dx;
            Pov = _profileF15Pov;
        }
        else
        {
            Dx = _profileDefaultF16Dx;
            Pov = _profileDefaultF16Pov;
        }

        CurrentAvionicsProfile = avionicsProfile;
    }

    private static bool IsF15(string? profile)
        => string.Equals(profile, F15ProfileTag, StringComparison.OrdinalIgnoreCase);

    public sealed class DxButton
    {
        public DxAssgn[] Assign { get; } = new DxAssgn[4];

        public DxButton(DxAssgn a0, DxAssgn a1, DxAssgn a2, DxAssgn a3)
        {
            Assign[0] = a0;
            Assign[1] = a1;
            Assign[2] = a2;
            Assign[3] = a3;
        }
    }

    public sealed record DxAssgn(string Callback, string Invoke, int SoundId);

    public sealed class PovHat
    {
        public PovDir[] Direction { get; } = new PovDir[8];

        public PovHat(PovDir[] dirs)
        {
            if (dirs.Length != 8) throw new ArgumentException("POV dirs must be length 8.");
            for (int i = 0; i < 8; i++) Direction[i] = dirs[i];
        }
    }

    public sealed record PovDir(string CallbackUnshift, string CallbackShift, int SoundIdUnshift, int SoundIdShift);

    public string KeyMappingPreviewDX(KeyAssgn keyAssign)
    {
        string result = "";

        for (int i = 0; i < Dx.Length; i++)
        {
            for (int ii = 0; ii < Dx[i].Assign.Length; ii++)
            {
                string cb = Dx[i].Assign[ii].Callback;
                if (cb == "SimDoNothing")
                    continue;
                if (keyAssign.GetCallback() != cb)
                    continue;

                if (result != "")
                    result += "\n";

                result += " DX" + (i + 1);

                if (ii == 1)
                    result += " SHIFT";
                if (ii == 2)
                    result += " RELEASE";
                if (ii == 3)
                    result += " RELEASE SHIFT";

                if (Dx[i].Assign[ii].Invoke == "Down" && ii != 2 && ii != 3)
                    result += " HOLD";
            }
        }

        return result;
    }

    public string KeyMappingPreviewPOV(KeyAssgn keyAssign)
    {
        string result = "";

        for (int i = 0; i < Pov.Length; i++)
        {
            for (int ii = 0; ii < Pov[i].Direction.Length; ii++)
            {
                string direction = GetDirectionLabel(ii);

                for (int iii = 0; iii < 2; iii++)
                {
                    string cb = (iii == 0) ? Pov[i].Direction[ii].CallbackUnshift : Pov[i].Direction[ii].CallbackShift;
                    if (cb == "SimDoNothing")
                        continue;

                    if (keyAssign.GetCallback() != cb)
                        continue;

                    if (result != "")
                        result += "\n";

                    result += " POV" + (i + 1) + "." + direction;
                    if (iii == 1)
                        result += " SHFT";
                }
            }
        }

        return result;
    }

    public JoyAssgnLite CloneDeep()
    {
        DxButton[] CloneDx(DxButton[] src)
        {
            var arr = new DxButton[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                arr[i] = new DxButton(
                    new DxAssgn(src[i].Assign[0].Callback, src[i].Assign[0].Invoke, src[i].Assign[0].SoundId),
                    new DxAssgn(src[i].Assign[1].Callback, src[i].Assign[1].Invoke, src[i].Assign[1].SoundId),
                    new DxAssgn(src[i].Assign[2].Callback, src[i].Assign[2].Invoke, src[i].Assign[2].SoundId),
                    new DxAssgn(src[i].Assign[3].Callback, src[i].Assign[3].Invoke, src[i].Assign[3].SoundId));
            }
            return arr;
        }

        PovHat[] ClonePov(PovHat[] src)
        {
            var arr = new PovHat[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var dirs = new PovDir[8];
                for (int d = 0; d < 8; d++)
                {
                    dirs[d] = new PovDir(
                        src[i].Direction[d].CallbackUnshift,
                        src[i].Direction[d].CallbackShift,
                        src[i].Direction[d].SoundIdUnshift,
                        src[i].Direction[d].SoundIdShift);
                }
                arr[i] = new PovHat(dirs);
            }
            return arr;
        }

        return new JoyAssgnLite(
            ProductName,
            dxActive: CloneDx(Dx),
            povActive: ClonePov(Pov),
            profileDefaultF16Dx: CloneDx(_profileDefaultF16Dx),
            profileDefaultF16Pov: ClonePov(_profileDefaultF16Pov),
            profileF15Dx: CloneDx(_profileF15Dx),
            profileF15Pov: ClonePov(_profileF15Pov)
        )
        {
            CurrentAvionicsProfile = CurrentAvionicsProfile
        };
    }

    public void ClearCallbackEverywhere(string callbackName)
    {
        for (int b = 0; b < Dx.Length; b++)
        {
            for (int i = 0; i < Dx[b].Assign.Length; i++)
            {
                if (string.Equals(Dx[b].Assign[i].Callback, callbackName, StringComparison.OrdinalIgnoreCase))
                {
                    Dx[b].Assign[i] = new DxAssgn("SimDoNothing", "Default", 0);
                }
            }
        }

        for (int h = 0; h < Pov.Length; h++)
        {
            for (int d = 0; d < Pov[h].Direction.Length; d++)
            {
                var cur = Pov[h].Direction[d];

                var un = cur.CallbackUnshift;
                var sh = cur.CallbackShift;

                int unSnd = cur.SoundIdUnshift;
                int shSnd = cur.SoundIdShift;

                if (string.Equals(un, callbackName, StringComparison.OrdinalIgnoreCase))
                {
                    un = "SimDoNothing";
                    unSnd = 0;
                }

                if (string.Equals(sh, callbackName, StringComparison.OrdinalIgnoreCase))
                {
                    sh = "SimDoNothing";
                    shSnd = 0;
                }

                Pov[h].Direction[d] = new PovDir(un, sh, unSnd, shSnd);
            }
        }
    }

    public string? GetDxCallback(int buttonIndex0Based, int assignIndex)
    {
        if (buttonIndex0Based < 0 || buttonIndex0Based >= Dx.Length) return null;
        if (assignIndex < 0 || assignIndex >= Dx[buttonIndex0Based].Assign.Length) return null;
        return Dx[buttonIndex0Based].Assign[assignIndex].Callback;
    }

    public void SetDxAssignment(int buttonIndex0Based, int assignIndex, string callbackName, string invoke, int soundId)
    {
        Dx[buttonIndex0Based].Assign[assignIndex] = new DxAssgn(callbackName, invoke, soundId);
    }

    public static string GetDirectionLabel(int dirId)
    {
        return dirId switch
        {
            0 => "UP",
            1 => "UR",
            2 => "R",
            3 => "DR",
            4 => "D",
            5 => "DL",
            6 => "L",
            7 => "UL",
            _ => "?"
        };
    }

    public string GetKeyLineDX(int indexInDeviceSortingOrder, int countDevices)
    {
        const int DXnumber = 128;

        var sb = new StringBuilder(4096);
        sb.Append("\n#======== ").Append(ProductName).Append(" ========\n");

        for (int i = 0; i < Dx.Length; i++)
        {
            for (int ii = 0; ii < Dx[i].Assign.Length; ii++)
            {
                var a = Dx[i].Assign[ii];
                if (a.Callback == "SimDoNothing")
                    continue;

                if (a.Callback == "SimHotasPinkyShift" || a.Callback == "SimHotasShift")
                {
                    if (ii != 0)
                        continue;

                    sb.Append(a.Callback);
                    sb.Append(' ').Append(indexInDeviceSortingOrder * DXnumber + i);
                    sb.Append(' ').Append(-1);
                    sb.Append(" -2 0 0x0 ").Append(a.SoundId.ToString(CultureInfo.InvariantCulture)).Append('\n');

                    sb.Append(a.Callback);
                    sb.Append(' ').Append(countDevices * DXnumber + indexInDeviceSortingOrder * DXnumber + i);
                    sb.Append(' ').Append(-1);
                    sb.Append(" -2 0 0x0 ").Append(a.SoundId.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    continue;
                }

                sb.Append(a.Callback);
                sb.Append(' ');

                bool shifted = (ii == 1 || ii == 3);
                int dxNum = (shifted ? countDevices * DXnumber : 0) + indexInDeviceSortingOrder * DXnumber + i;
                sb.Append(dxNum);

                sb.Append(' ').Append(InvokeToInt(a.Invoke));

                sb.Append(" -2 ");

                sb.Append((ii == 2 || ii == 3) ? "0x42" : "0");

                sb.Append(" 0x0 ").Append(a.SoundId.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        return sb.ToString();
    }

    public string GetKeyLinePOV(int povBase, int hatId)
    {
        var sb = new StringBuilder(2000);
        sb.AppendLine("\n");
        sb.AppendLine($"#======== {ProductName} : POV #{povBase} ========");

        for (int dirId = 0; dirId < Pov[hatId].Direction.Length; dirId++)
        {
            for (int shiftState = 0; shiftState < 2; shiftState++)
            {
                string callback = (shiftState == 0)
                    ? Pov[hatId].Direction[dirId].CallbackUnshift
                    : Pov[hatId].Direction[dirId].CallbackShift;

                int soundId = (shiftState == 0)
                    ? Pov[hatId].Direction[dirId].SoundIdUnshift
                    : Pov[hatId].Direction[dirId].SoundIdShift;

                int povNumShifted = (shiftState == 1) ? (povBase + 2) : povBase;

                if (callback == "SimDoNothing")
                    sb.Append("# ");

                sb.AppendLine($"{callback} {povNumShifted} -1 -3 {dirId} 0x0 {soundId.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return sb.ToString();
    }

    private static int InvokeToInt(string invoke)
    {
        return invoke switch
        {
            "Default" => -1,
            "Down" => -2,
            "Up" => -4,
            "UI" => 8,
            _ => -1
        };
    }
}