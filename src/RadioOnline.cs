// RadioOnline: internet radio for GTA V (ScriptHookVDotNet 3 + NAudio 1.10).
// Plays MP3 internet streams while the player is in a vehicle and keeps the game radio off.
// Stations and keys come from scripts\RadioOnline.ini. Written in C# 5 (built with the .NET Framework csc).
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using NAudio.Wave;

public class RadioOnline : Script
{
    private class Station
    {
        public string Name;
        public string Url;
    }

    private readonly List<Station> stations = new List<Station>();
    private Keys keyToggle, keyNext, keyPrev, keyVolUp, keyVolDown;
    private float volume;
    private bool enabled;          // user wants online radio
    private int current;
    private Player streamPlayer;   // background stream for the current station
    private int lastRadioOff;

    public RadioOnline()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        LoadSettings();
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += delegate { StopStream(); };
        Interval = 250;
    }

    private void LoadSettings()
    {
        ScriptSettings s = ScriptSettings.Load(Path.Combine(BaseDirectory, "RadioOnline.ini"));
        keyToggle = ParseKey(s.GetValue("KEYS", "Toggle", "Home"), Keys.Home);
        keyNext = ParseKey(s.GetValue("KEYS", "Next", "PageUp"), Keys.PageUp);
        keyPrev = ParseKey(s.GetValue("KEYS", "Previous", "PageDown"), Keys.PageDown);
        keyVolUp = ParseKey(s.GetValue("KEYS", "VolumeUp", "Add"), Keys.Add);
        keyVolDown = ParseKey(s.GetValue("KEYS", "VolumeDown", "Subtract"), Keys.Subtract);
        volume = Clamp(s.GetValue("SETTINGS", "Volume", 60) / 100f);
        enabled = s.GetValue("SETTINGS", "StartEnabled", false);
        // ScriptSettings cuts values at ':' (it breaks URLs), so the stations are read straight from the file
        string[] lines = new string[0];
        try { lines = File.ReadAllLines(Path.Combine(BaseDirectory, "RadioOnline.ini")); } catch { }
        bool inStations = false;
        foreach (string raw in lines)
        {
            string l = raw.Trim();
            if (l.StartsWith("[")) { inStations = l.Equals("[STATIONS]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inStations || l.StartsWith(";") || l.Length == 0) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string line = l.Substring(eq + 1);
            int bar = line.IndexOf('|');
            if (bar <= 0) continue;
            Station st = new Station();
            st.Name = line.Substring(0, bar).Trim();
            st.Url = line.Substring(bar + 1).Trim();
            if (st.Url.Length > 0) stations.Add(st);
        }
    }

    private static Keys ParseKey(string value, Keys fallback)
    {
        try { return (Keys)Enum.Parse(typeof(Keys), value, true); }
        catch { return fallback; }
    }

    private static float Clamp(float v)
    {
        return v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (stations.Count == 0) return;
        if (e.KeyCode == keyToggle)
        {
            enabled = !enabled;
            if (!enabled) { StopStream(); RestoreGameRadio(); Show("Radio online: apagada"); }
            else Show("Radio online: encendida");
        }
        else if (!enabled) return;
        else if (e.KeyCode == keyNext) { current = (current + 1) % stations.Count; StopStream(); }
        else if (e.KeyCode == keyPrev) { current = (current - 1 + stations.Count) % stations.Count; StopStream(); }
        else if (e.KeyCode == keyVolUp) { volume = Clamp(volume + 0.1f); ApplyVolume(); Show("Volumen: " + (int)Math.Round(volume * 100) + "%"); }
        else if (e.KeyCode == keyVolDown) { volume = Clamp(volume - 0.1f); ApplyVolume(); Show("Volumen: " + (int)Math.Round(volume * 100) + "%"); }
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (!enabled || stations.Count == 0) return;
        Ped ped = Game.Player.Character;
        Vehicle veh = ped != null && ped.Exists() ? ped.CurrentVehicle : null;
        bool inCar = veh != null && veh.Exists() && !Game.IsPaused && !ped.IsDead;
        if (!inCar)
        {
            StopStream();
            return;
        }
        // keep the game's own radio off while the online one plays
        if (Game.GameTime - lastRadioOff > 1000)
        {
            Function.Call(Hash.SET_VEH_RADIO_STATION, veh, "OFF");
            lastRadioOff = Game.GameTime;
        }
        if (streamPlayer == null)
        {
            streamPlayer = new Player(stations[current].Url, volume);
            Show("~b~Radio online~s~: " + stations[current].Name);
        }
        else if (streamPlayer.Failed && !failShown)
        {
            // stay on this station (no auto-skip loop); show the reason and log it
            failShown = true;
            Show("~r~No se pudo conectar~s~: " + stations[current].Name + " (" + streamPlayer.Error + ")");
            Log(stations[current].Name + " " + stations[current].Url + "\r\n" + streamPlayer.ErrorFull);
        }
    }

    private void ApplyVolume()
    {
        if (streamPlayer != null) streamPlayer.SetVolume(volume);
    }

    private void StopStream()
    {
        if (streamPlayer != null) { streamPlayer.Stop(); streamPlayer = null; }
        failShown = false;
    }

    private bool failShown;

    // %TEMP%\RadioOnline.log (the game folder is admin-only, so the game cannot write there)
    private static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "RadioOnline.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + "\r\n\r\n"); }
        catch { }
    }

    private void RestoreGameRadio()
    {
        Ped ped = Game.Player.Character;
        if (ped != null && ped.Exists() && ped.CurrentVehicle != null)
            Function.Call(Hash.SET_VEH_RADIO_STATION, ped.CurrentVehicle, "RADIO_01_CLASS_ROCK");
    }

    private static void Show(string text)
    {
        GTA.UI.Notification.Show(text);
    }

    // Streams one MP3 internet station on a background thread (NAudio MP3 streaming pattern).
    private class Player
    {
        private readonly string url;
        private volatile bool stopping;
        private volatile float volume;
        private WaveOutEvent output;
        private readonly object sync = new object();
        public volatile bool Failed;
        public string Error = "";
        public string ErrorFull = "";

        public Player(string url, float volume)
        {
            this.url = url;
            this.volume = volume;
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Start();
        }

        public void SetVolume(float v)
        {
            volume = v;
            lock (sync) { if (output != null) output.Volume = v; }
        }

        public void Stop()
        {
            stopping = true;
        }

        private void Run()
        {
            HttpWebResponse response = null;
            IMp3FrameDecompressor decompressor = null;
            BufferedWaveProvider buffer = null;
            byte[] pcm = new byte[16384 * 4];
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0";
                req.Timeout = 10000;
                req.AllowAutoRedirect = true;
                response = (HttpWebResponse)req.GetResponse();
                Stream net = response.GetResponseStream();
                ReadFullyStream stream = new ReadFullyStream(net);
                while (!stopping)
                {
                    if (buffer != null && buffer.BufferedDuration.TotalSeconds > 4)
                    {
                        Thread.Sleep(200);
                        continue;
                    }
                    Mp3Frame frame = Mp3Frame.LoadFromStream(stream);
                    if (frame == null) break;
                    if (decompressor == null)
                    {
                        WaveFormat fmt = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2, frame.FrameLength, frame.BitRate);
                        decompressor = new AcmMp3FrameDecompressor(fmt);
                        buffer = new BufferedWaveProvider(decompressor.OutputFormat);
                        buffer.BufferDuration = TimeSpan.FromSeconds(10);
                        buffer.DiscardOnBufferOverflow = true;
                    }
                    int n = decompressor.DecompressFrame(frame, pcm, 0);
                    buffer.AddSamples(pcm, 0, n);
                    if (output == null && buffer.BufferedDuration.TotalSeconds > 1.5)
                    {
                        lock (sync)
                        {
                            output = new WaveOutEvent();
                            output.Init(buffer);
                            output.Volume = volume;
                            output.Play();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Exception root = ex;
                while (root.InnerException != null) root = root.InnerException;
                Error = root.GetType().Name + ": " + root.Message;
                if (Error.Length > 90) Error = Error.Substring(0, 90);
                ErrorFull = ex.ToString();
                if (!stopping) Failed = true;
            }
            finally
            {
                lock (sync)
                {
                    if (output != null) { try { output.Stop(); output.Dispose(); } catch { } output = null; }
                }
                if (decompressor != null) { try { decompressor.Dispose(); } catch { } }
                if (response != null) { try { response.Close(); } catch { } }
            }
        }
    }

    // Network streams return partial reads; Mp3Frame needs full reads.
    private class ReadFullyStream : Stream
    {
        private readonly Stream src;
        public ReadFullyStream(Stream src) { this.src = src; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { return 0; } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = src.Read(buffer, offset + total, count - total);
                if (r == 0) break;
                total += r;
            }
            return total;
        }
    }
}
