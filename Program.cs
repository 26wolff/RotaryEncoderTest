using System.Device.Gpio;
using System.Diagnostics;

const int clkPin = 17;
const int dtPin = 27;
const int switchPin = 22;

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

Console.WriteLine("Rotary encoder test");
Console.WriteLine($"CLK: GPIO{clkPin}, DT: GPIO{dtPin}, SW: GPIO{switchPin}");
Console.WriteLine("Turn the encoder or press its button. Press Ctrl+C to exit.");

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
            Console.WriteLine("Rotated clockwise");
            quarterSteps = 0;
        }
        else if (quarterSteps <= -4)
        {
            Console.WriteLine("Rotated counterclockwise");
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
                Console.WriteLine("Button pressed");
            }
        }
    }

    await Task.Delay(1);
}

int ReadEncoderState() =>
    (gpio.Read(clkPin) == PinValue.High ? 2 : 0) |
    (gpio.Read(dtPin) == PinValue.High ? 1 : 0);
