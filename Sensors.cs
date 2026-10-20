using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware;

internal sealed class Readings
{
    public double? Cpu, Memory, CpuTemp, DiskTemp, GpuTemp, GpuLoad;
    public GpuReading[] Gpus = new GpuReading[0];
    public DateTime LoadAt, TemperatureAt;
    public string CpuSensor = "未读取", DiskSensor = "未读取", GpuSensor = "未读取", Error = "正在初始化温度传感器";
    public double TemperatureReadMs;
    public Readings Copy() { return (Readings)MemberwiseClone(); }
}

internal sealed class GpuReading
{
    public string Id, Name, Kind, Sensor;
    public double? Temperature, Load;
}

internal static class GpuSelection
{
    internal static bool IsGpu(HardwareType type)
    {
        return type==HardwareType.GpuNvidia || type==HardwareType.GpuAmd || type==HardwareType.GpuIntel;
    }
    internal static bool IsCoreTemperature(string name)
    {
        return name.Equals("GPU Core",StringComparison.OrdinalIgnoreCase)
            || name.Equals("GPU",StringComparison.OrdinalIgnoreCase);
    }
    internal static GpuReading Choose(GpuReading[] gpus)
    {
        // Prefer NVIDIA/AMD over Intel; identifiers and every adapter remain visible in details.
        // Do not silently substitute a hot-spot or VRAM temperature for the GPU core.
        GpuReading[] preferred=gpus.Where(g=>g.Kind!="GpuIntel").ToArray();
        if(preferred.Length==0) preferred=gpus;
        return preferred.OrderByDescending(g=>g.Temperature.HasValue)
            .ThenByDescending(g=>g.Temperature ?? double.MinValue)
            .ThenBy(g=>g.Id,StringComparer.Ordinal).FirstOrDefault();
    }
}

internal sealed class SensorService : IDisposable
{
    private readonly object gate = new object();
    private readonly Readings readings = new Readings();
    private readonly ManualResetEvent stop = new ManualResetEvent(false);
    private readonly Thread loads, temperatures;
    private readonly int loadMs, temperatureMs;
    private int refreshGpu;
    internal void RefreshGpu() { Interlocked.Exchange(ref refreshGpu,1); }
    public SensorService(int loadIntervalMs, int temperatureIntervalMs)
    {
        loadMs = loadIntervalMs; temperatureMs = temperatureIntervalMs;
        loads = new Thread(LoadLoop) { IsBackground = true, Name = "Load sampler" };
        temperatures = new Thread(TemperatureLoop) { IsBackground = true, Name = "Temperature sampler" };
        loads.Start(); temperatures.Start();
    }
    public Readings Snapshot()
    {
        lock (gate)
        {
            Readings result = readings.Copy();
            if ((DateTime.UtcNow-result.LoadAt).TotalMilliseconds > Math.Max(5000,loadMs*3)) result.Cpu=result.Memory=null;
            if ((DateTime.UtcNow-result.TemperatureAt).TotalMilliseconds > Math.Max(15000,temperatureMs*3))
            {
                result.CpuTemp=result.DiskTemp=result.GpuTemp=result.GpuLoad=null;
                result.Gpus=result.Gpus.Select(g=>new GpuReading { Id=g.Id,Name=g.Name,Kind=g.Kind,Sensor="读数已过期" }).ToArray();
                result.GpuSensor="GPU 读数已过期";
            }
            return result;
        }
    }
    private void LoadLoop()
    {
        ulong oldIdle, oldKernel, oldUser;
        bool baseline = Native.GetSystemTimes(out oldIdle,out oldKernel,out oldUser);
        while (!stop.WaitOne(loadMs))
        {
            ulong idle,kernel,user;
            double? cpu=null, memory=null;
            bool valid=Native.GetSystemTimes(out idle,out kernel,out user);
            if (valid && baseline && kernel>=oldKernel && user>=oldUser && idle>=oldIdle)
            {
                ulong total=(kernel-oldKernel)+(user-oldUser), inactive=idle-oldIdle;
                if(total>0 && inactive<=total) cpu=100.0*(total-inactive)/total;
            }
            baseline=valid; oldIdle=idle; oldKernel=kernel; oldUser=user;
            Native.MemoryStatus status=new Native.MemoryStatus();
            status.Length=(uint)Marshal.SizeOf(typeof(Native.MemoryStatus));
            if(Native.GlobalMemoryStatusEx(ref status) && status.TotalPhys>0) memory=100.0*(status.TotalPhys-status.AvailPhys)/status.TotalPhys;
            lock(gate) { readings.Cpu=cpu; readings.Memory=memory; readings.LoadAt=DateTime.UtcNow; }
        }
    }
    private static bool Valid(float? value) { return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value) && value.Value>0 && value.Value<150; }
    private void TemperatureLoop()
    {
        Computer computer=null;
        try
        {
            computer=new Computer { IsCpuEnabled=true, IsStorageEnabled=true };
            computer.Open();
            DateTime nextGpuScan=DateTime.MinValue;
            foreach(IHardware hardware in computer.Hardware)
                foreach(ISensor sensor in hardware.Sensors) sensor.ValuesTimeWindow=TimeSpan.Zero;
            do
            {
                Stopwatch watch=Stopwatch.StartNew();
                double? cpu=null,disk=null;
                string cpuName="无可用 CPU 温度传感器",diskName="无可用磁盘温度传感器";
                List<string> errors=new List<string>();
                List<GpuReading> gpus=new List<GpuReading>();
                // LHM enumerates adapters when the GPU groups open. Reopen only those groups,
                // on the sampling thread, to discover docks connected after program startup.
                if(Interlocked.Exchange(ref refreshGpu,0)!=0 || DateTime.UtcNow>=nextGpuScan)
                {
                    try { computer.IsGpuEnabled=false; computer.IsGpuEnabled=true; }
                    catch(Exception ex) { errors.Add("GPU 重新检测："+ex.Message); }
                    nextGpuScan=DateTime.UtcNow.AddSeconds(30);
                }
                foreach(IHardware hardware in computer.Hardware)
                {
                    bool isGpu=GpuSelection.IsGpu(hardware.HardwareType);
                    if(hardware.HardwareType!=HardwareType.Cpu && hardware.HardwareType!=HardwareType.Storage && !isGpu) continue;
                    GpuReading gpu=null;
                    if(isGpu)
                    {
                        gpu=new GpuReading { Id=hardware.Identifier.ToString(),Name=hardware.Name,Kind=hardware.HardwareType.ToString(),Sensor="无可用 GPU 核心温度" };
                        gpus.Add(gpu);
                    }
                    try
                    {
                        hardware.Update();
                        foreach(ISensor sensor in hardware.Sensors) sensor.ValuesTimeWindow=TimeSpan.Zero;
                        ISensor[] candidates=hardware.Sensors.Where(s=>s.SensorType==SensorType.Temperature && Valid(s.Value)).ToArray();
                        if(isGpu)
                        {
                            ISensor chosen=candidates.FirstOrDefault(s=>GpuSelection.IsCoreTemperature(s.Name));
                            if(chosen!=null) { gpu.Temperature=chosen.Value.Value; gpu.Sensor=chosen.Name+" ("+chosen.Identifier+")"; }
                            ISensor load=hardware.Sensors.FirstOrDefault(s=>s.SensorType==SensorType.Load &&
                                (s.Name.Equals("GPU Core",StringComparison.OrdinalIgnoreCase) || s.Name.Equals("D3D 3D",StringComparison.OrdinalIgnoreCase)) &&
                                s.Value.HasValue && !float.IsNaN(s.Value.Value) && s.Value.Value>=0 && s.Value.Value<=100);
                            if(load!=null) gpu.Load=load.Value.Value;
                        }
                        else if(hardware.HardwareType==HardwareType.Cpu)
                        {
                            ISensor chosen=candidates.FirstOrDefault(s=>s.Name.IndexOf("Package",StringComparison.OrdinalIgnoreCase)>=0)
                                ?? candidates.OrderByDescending(s=>s.Value.Value).FirstOrDefault();
                            if(chosen!=null && (!cpu.HasValue || chosen.Value.Value>cpu.Value)) { cpu=chosen.Value.Value; cpuName=hardware.Name+" / "+chosen.Name+" ("+chosen.Identifier+")"; }
                        }
                        else
                        {
                            ISensor chosen=candidates.FirstOrDefault(s=>s.Name.Equals("Temperature",StringComparison.OrdinalIgnoreCase))
                                ?? candidates.FirstOrDefault(s=>s.Name.IndexOf("Composite",StringComparison.OrdinalIgnoreCase)>=0)
                                ?? candidates.FirstOrDefault();
                            if(chosen!=null && (!disk.HasValue || chosen.Value.Value>disk.Value)) { disk=chosen.Value.Value; diskName=hardware.Name+" / "+chosen.Name+" ("+chosen.Identifier+")"; }
                        }
                    }
                    catch(Exception ex) { errors.Add(hardware.Name+": "+ex.Message); }
                }
                watch.Stop();
                if(!cpu.HasValue) errors.Add("CPU 温度不可用：检查 PawnIO 驱动、管理员权限或硬件支持");
                if(!disk.HasValue) errors.Add("磁盘温度不可用：检查管理员权限或硬件支持");
                GpuReading selected=GpuSelection.Choose(gpus.ToArray());
                lock(gate)
                {
                    readings.CpuTemp=cpu; readings.DiskTemp=disk; readings.CpuSensor=cpuName; readings.DiskSensor=diskName;
                    readings.Gpus=gpus.ToArray(); readings.GpuTemp=selected==null?null:selected.Temperature;
                    readings.GpuLoad=selected==null?null:selected.Load;
                    readings.GpuSensor=selected==null?"未检测到支持的 GPU":selected.Name+" / "+selected.Sensor;
                    readings.Error=string.Join("\n",errors); readings.TemperatureAt=DateTime.UtcNow; readings.TemperatureReadMs=watch.Elapsed.TotalMilliseconds;
                }
            } while(!stop.WaitOne(temperatureMs));
        }
        catch(Exception ex) { lock(gate) { readings.Error=ex.ToString(); readings.CpuTemp=readings.DiskTemp=readings.GpuTemp=readings.GpuLoad=null; readings.Gpus=new GpuReading[0]; } }
        finally { if(computer!=null) try { computer.Close(); } catch {} }
    }
    public void Dispose()
    {
        stop.Set(); loads.Join(2000);
        // Do not block the UI indefinitely if a device driver fails to return.
        if(temperatures.Join(2000)) stop.Dispose();
    }
}
