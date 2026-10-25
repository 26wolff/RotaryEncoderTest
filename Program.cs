using System.Device.Gpio;
using System.Diagnostics;

const int clkPin = 17;
const int dtPin = 27;
const int switchPin = 22;
const int initialBrightness = 100;

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

var backlight = BacklightController.Open();
var brightness = initialBrightness;
backlight.SetBrightness(brightness);

Console.WriteLine("Rotary encoder test");
Console.WriteLine($"CLK: GPIO{clkPin}, DT: GPIO{dtPin}, SW: GPIO{switchPin}");
Console.WriteLine($"Backlight: {backlight.Name} ({brightness}%)");
Console.WriteLine("Turn the encoder to change brightness. Press its button to show the current value. Press Ctrl+C to exit.");

using var gpio = new GpioController();
gpio.OpenPin(clkPin, PinMode.InputPullUp);
gpio.OpenPin(dtPin, PinMode.InputPullUp);
gpio.OpenPin(switchPin, PinMode.InputPullUp);

var previousEncoderState = ReadEncoderState();
var previousSwitchState = gpio.Read(switchPin);
var lastSwitchChange = Stopwatch.GetTimestamp();
var quarterSteps = 0;
var transitions = new[]
{
    0, 1, -1, 0,
    -1, 0, 0, 1,
    1, 0, 0, -1,
    0, -1, 1, 0,
};

while (!cancellationTokenSource.IsCancellationRequested)
{
    var encoderState = ReadEncoderState();
    var transition = transitions[(previousEncoderState << 2) | encoderState];
    if (transition != 0)
    {
        quarterSteps += transition;
        if (quarterSteps >= 4)
        {
            brightness = Math.Min(100, brightness + 1);
            backlight.SetBrightness(brightness);
            Console.WriteLine($"Rotated clockwise: {brightness}%");
            quarterSteps = 0;
        }
        else if (quarterSteps <= -4)
        {
            brightness = Math.Max(0, brightness - 1);
            backlight.SetBrightness(brightness);
            Console.WriteLine($"Rotated counterclockwise: {brightness}%");
            quarterSteps = 0;
        }
    }
    else if (encoderState != previousEncoderState)
    {
        quarterSteps = 0;
    }

    previousEncoderState = encoderState;

    var switchState = gpio.Read(switchPin);
    if (switchState != previousSwitchState)
    {
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(lastSwitchChange, now) >= TimeSpan.FromMilliseconds(30))
        {
            previousSwitchState = switchState;
            lastSwitchChange = now;
            if (switchState == PinValue.Low)
            {
                Console.WriteLine($"Button pressed: brightness is {brightness}%");
            }
        }
    }

    await Task.Delay(1);
}

int ReadEncoderState() =>
    (gpio.Read(clkPin) == PinValue.High ? 2 : 0) |
    (gpio.Read(dtPin) == PinValue.High ? 1 : 0);

sealed class BacklightController
{
    private readonly string _brightnessPath;
    private readonly int _maximumBrightness;

    private BacklightController(string name, string brightnessPath, int maximumBrightness)
    {
        Name = name;
        _brightnessPath = brightnessPath;
        _maximumBrightness = maximumBrightness;
    }

    public string Name { get; }

    public static BacklightController Open()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The rotary backlight test requires Linux backlight sysfs.");
        }

        const string backlightDirectory = "/sys/class/backlight";
        if (!Directory.Exists(backlightDirectory))
        {
            throw new DirectoryNotFoundException($"No Linux backlight devices were found at {backlightDirectory}.");
        }

        var devices = Directory.GetDirectories(backlightDirectory);
        if (devices.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one Linux backlight device in {backlightDirectory}, but found {devices.Length}.");
        }

        var deviceDirectory = devices[0];
        var maximumBrightness = int.Parse(File.ReadAllText(Path.Combine(deviceDirectory, "max_brightness")).Trim());
        if (maximumBrightness <= 0)
        {
            throw new InvalidOperationException($"Backlight device '{Path.GetFileName(deviceDirectory)}' has an invalid maximum brightness.");
        }

        return new BacklightController(
            Path.GetFileName(deviceDirectory),
            Path.Combine(deviceDirectory, "brightness"),
            maximumBrightness);
    }

    public void SetBrightness(int percentage)
    {
        var deviceBrightness = (percentage * _maximumBrightness + 50) / 100;
        File.WriteAllText(_brightnessPath, deviceBrightness.ToString());
    }
}
