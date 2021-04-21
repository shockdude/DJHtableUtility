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
            while (!shouldStop)
            {
                // Read 27 bytes from the turntable
                int bytesRead;
                byte[] readBuffer = new byte[27];
                var reader = device.OpenEndpointReader(ReadEndpointID.Ep01);
                reader.Read(readBuffer, 100, out bytesRead);

                // Prevent default 0x00 when no bytes are read
                if (bytesRead > 0)
                {
                    // Set table buttons to DJMAX 6B controls
                    byte buttons = readBuffer[23];
                    controller.SetButtonState(Xbox360Button.Left, (buttons & 0x40) != 0x00 || readBuffer[2] == 5 || readBuffer[2] == 6 || readBuffer[2] == 7); // left blue & dpad left
                    controller.SetButtonState(Xbox360Button.Up, (buttons & 0x20) != 0x00 || readBuffer[2] == 7 || readBuffer[2] == 0 || readBuffer[2] == 1); // left red & dpad up
                    controller.SetButtonState(Xbox360Button.Right, (buttons & 0x10) != 0x00 || readBuffer[2] == 1 || readBuffer[2] == 2 || readBuffer[2] == 3); // left green & dpad right
                    controller.SetButtonState(Xbox360Button.X, (buttons & 0x01) != 0x00); // right green
                    controller.SetButtonState(Xbox360Button.Y, (buttons & 0x02) != 0x00); // right red
                    controller.SetButtonState(Xbox360Button.B, (buttons & 0x04) != 0x00); // right blue

                    // Set euphoria button to fever
                    // Set a fast scratch to also trigger fever
                    buttons = readBuffer[0];
                    controller.SetButtonState(Xbox360Button.A, (buttons & 0x08) != 0x00 || readBuffer[5] > 132 || readBuffer[5] < 124 || readBuffer[6] > 132 || readBuffer[6] < 124); // euphoria

                    // Set the start/select/ps buttons
                    buttons = readBuffer[1];
                    controller.SetButtonState(Xbox360Button.Start, (buttons & 0x02) != 0x00); // Start
                    controller.SetButtonState(Xbox360Button.Back, (buttons & 0x01) != 0x00); // Select
                    controller.SetButtonState(Xbox360Button.Guide, (buttons & 0x10) != 0x00); // Sync Button

                    // Set crossfader axis
                    int faderValue = (((readBuffer[21] & 0xF0) << 6) | ((readBuffer[22] & 0x03) << 14)) - 32768;
                    controller.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(faderValue > 16384 || faderValue < -16384 ? faderValue : 0));

                    // Set DPAD
                    controller.SetButtonState(Xbox360Button.Down, readBuffer[2] == 3 || readBuffer[2] == 4 || readBuffer[2] == 5);
                }
            }
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
