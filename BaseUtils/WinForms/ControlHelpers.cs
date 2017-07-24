﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.Form;

public static class ControlHelpersStaticFunc
{
    // DO a refresh after this. presumes you have sorted the order of controls added in the designer file
    // from C, offset either up/down dependent on on.  Remember in tag of c direction you shifted.  Don't shift if in same direction
    // useful for autolayout forms
    static public void ShiftControls(this Control.ControlCollection coll, Control c, int offset, bool on)
    {
        bool enabled = false;
        bool prevon = false;
        foreach (Control ctrl in coll)
        {
            if (ctrl == c)
            {
                prevon = (ctrl.Tag == null) ? true : (bool)ctrl.Tag;
                ctrl.Tag = on;
                enabled = prevon != on;
                //System.Diagnostics.Debug.WriteLine("Decided for enable " + enabled + " to " + on);
            }

            if (enabled)
            {
                ctrl.Location = new Point(ctrl.Left, ctrl.Top + ((on) ? offset : -offset));
                //System.Diagnostics.Debug.WriteLine("Control " + ctrl.Name + " to " + ctrl.Location + " offset " + offset + " on " + on);
            }
        }
    }

    static public StringFormat StringFormatFromContentAlignment(ContentAlignment c)
    {
        StringFormat f = new StringFormat();
        if (c == ContentAlignment.BottomCenter || c == ContentAlignment.MiddleCenter || c == ContentAlignment.TopCenter)
            f.Alignment = StringAlignment.Center;
        else if (c == ContentAlignment.BottomLeft || c == ContentAlignment.MiddleLeft || c == ContentAlignment.TopLeft)
            f.Alignment = StringAlignment.Near;
        else
            f.Alignment = StringAlignment.Far;

        if (c == ContentAlignment.BottomCenter || c == ContentAlignment.BottomLeft || c == ContentAlignment.BottomRight)
            f.LineAlignment = StringAlignment.Far;
        else if (c == ContentAlignment.MiddleLeft || c == ContentAlignment.MiddleCenter || c == ContentAlignment.MiddleRight)
            f.LineAlignment = StringAlignment.Center;
        else
            f.LineAlignment = StringAlignment.Near;

        return f;
    }

    static public Rectangle ImagePositionFromContentAlignment(ContentAlignment c, Rectangle client, Size image)
    {
        int left = client.Left;

        if (c == ContentAlignment.BottomCenter || c == ContentAlignment.MiddleCenter || c == ContentAlignment.TopCenter)
            left += Math.Max((client.Width - image.Width) / 2, 0);
        else if (c == ContentAlignment.BottomLeft || c == ContentAlignment.MiddleLeft || c == ContentAlignment.TopLeft)
            left += 0;
        else
            left += Math.Max(client.Width - image.Width, 0);

        int top = client.Top;

        if (c == ContentAlignment.BottomCenter || c == ContentAlignment.BottomLeft || c == ContentAlignment.BottomRight)
            top += Math.Max(client.Height - image.Height, 0);
        else if (c == ContentAlignment.MiddleLeft || c == ContentAlignment.MiddleCenter || c == ContentAlignment.MiddleRight)
            top += Math.Max((client.Height - image.Height) / 2, 0);
        else
            top += 0;

        return new Rectangle(left, top, image.Width, image.Height);
    }
}