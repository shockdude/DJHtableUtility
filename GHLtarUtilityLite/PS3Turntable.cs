using LibUsbDotNet;
using LibUsbDotNet.Main;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;

namespace GHLtarUtilityLite
{
    class PS3Turntable : PS3Peripheral
    {
        System.Threading.Thread t;
        private bool shouldStop;

        public PS3Turntable(UsbDevice dongle, IXbox360Controller newController)
        {
            device = dongle;
            controller = newController;

            // Thread to constantly read inputs
            t = new System.Threading.Thread(new System.Threading.ThreadStart(updateRoutine));
            t.Start();

            controller.Connect();
        }

        public override bool isReadable()
        {
            // If device isn't open (closes itself), assume disconnected.
            if (!device.IsOpen) return false;
            if (!device.UsbRegistryInfo.IsAlive) return false;
            return true;
        }

        private static short ClampShort(int value)
        {
            return (short)((value < short.MinValue) ? short.MinValue : (value > short.MaxValue) ? short.MaxValue : value);
        }

        private void updateRoutine()
        {
            int bytesRead;
            var reader = device.OpenEndpointReader(ReadEndpointID.Ep01);
            byte[] readBuffer = new byte[27];

            UsbSetupPacket setupPacket = new UsbSetupPacket(0x21, 0x09, 0x0201, 0x00, 0x08);
            int bytesWritten;
            byte[] writeBuffer = new byte[8];
            byte buttons = 0x00;

            byte euphoriaState = 0x00;
            byte playerLEDState = 0x01;

            // FIXME event handler not being called during rumble, probably did something wrong.
            Xbox360FeedbackReceivedEventHandler onXbox360FeedbackReceived = (sender, eventArgs) =>
            {
                if (eventArgs.SmallMotor > 0)
                {
                    euphoriaState = 0x01;
                }
                else
                {
                    euphoriaState = 0x00;
                }
                playerLEDState = eventArgs.LedNumber;
                Console.WriteLine("smallMotor: {0}, playerLED = {1}", eventArgs.SmallMotor, eventArgs.LedNumber);
            };

            controller.FeedbackReceived += onXbox360FeedbackReceived;

            while (!shouldStop)
            {
                // Read 27 bytes from the turntable
                reader.Read(readBuffer, 100, out bytesRead);

                // Prevent default 0x00 when no bytes are read
                if (bytesRead > 0)
                {
                    // Set the table buttons on the virtual 360 controller
                    buttons = readBuffer[0];
                    controller.SetButtonState(Xbox360Button.A, (buttons & 0x02) != 0x00); // green
                    controller.SetButtonState(Xbox360Button.B, (buttons & 0x04) != 0x00); // red
                    controller.SetButtonState(Xbox360Button.X, (buttons & 0x01) != 0x00); // blue
                    controller.SetButtonState(Xbox360Button.Y, (buttons & 0x08) != 0x00); // euphoria

                    // Set the start/select/ps buttons
                    buttons = readBuffer[1];
                    controller.SetButtonState(Xbox360Button.Start, (buttons & 0x02) != 0x00); // Start
                    controller.SetButtonState(Xbox360Button.Back, (buttons & 0x01) != 0x00); // Select
                    controller.SetButtonState(Xbox360Button.Guide, (buttons & 0x10) != 0x00); // Sync Button

                    // Set turntable axes
                    // double the axis value to increase sensitivity
                    controller.SetAxisValue(Xbox360Axis.LeftThumbX, ClampShort((readBuffer[5] << 9) - 65536));
                    controller.SetAxisValue(Xbox360Axis.LeftThumbY, ClampShort((readBuffer[6] << 9) - 65536));

                    // Set effects axis
                    controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)((((readBuffer[19] & 0xF0) << 6) | ((readBuffer[20] & 0x03) << 14)) - 32768));

                    // Set crossfader axis
                    controller.SetAxisValue(Xbox360Axis.RightThumbY, (short)((((readBuffer[21] & 0xF0) << 6) | ((readBuffer[22] & 0x03) << 14)) - 32768));

                    // Set DPAD
                    controller.SetButtonState(Xbox360Button.Up, readBuffer[2] == 7 || readBuffer[2] == 0 || readBuffer[2] == 1);
                    controller.SetButtonState(Xbox360Button.Right, readBuffer[2] == 1 || readBuffer[2] == 2 || readBuffer[2] == 3);
                    controller.SetButtonState(Xbox360Button.Down, readBuffer[2] == 3 || readBuffer[2] == 4 || readBuffer[2] == 5);
                    controller.SetButtonState(Xbox360Button.Left, readBuffer[2] == 5 || readBuffer[2] == 6 || readBuffer[2] == 7);
                }

                // Write 8 bytes to the turntable 2x times

                // Euphoria LED
                writeBuffer[0] = 0x91;
                writeBuffer[1] = 0x01;
                // 1 to enable euphoria LED, 0 to disable euphoria LED
                writeBuffer[2] = euphoriaState;
                writeBuffer[3] = 0x00;
                writeBuffer[4] = 0x00;
                writeBuffer[5] = 0x00;
                writeBuffer[6] = 0x00;
                writeBuffer[7] = 0x00;
                device.ControlTransfer(ref setupPacket, writeBuffer, 8, out bytesWritten);

                // Player LEDs
                writeBuffer[0] = 0x01;
                writeBuffer[1] = 0x08;
                // 0x1 for P1, 0x2 for P2, 0x4 for P3, 0x8 for P4
                writeBuffer[2] = playerLEDState;
                writeBuffer[3] = 0x00;
                writeBuffer[4] = 0x00;
                writeBuffer[5] = 0x00;
                writeBuffer[6] = 0x00;
                writeBuffer[7] = 0x00;
                device.ControlTransfer(ref setupPacket, writeBuffer, 8, out bytesWritten);
            }

            controller.FeedbackReceived -= onXbox360FeedbackReceived;
        }

        public override void destroy()
        {
            // Destroy EVERYTHING.
            shouldStop = true;
            try { controller.Disconnect(); } catch (Exception) { }
            t.Abort();
            device.Close();
        }
    }
}
