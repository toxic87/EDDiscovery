﻿/*
 * Copyright © 2019-2023 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using EliteDangerousCore;
using EliteDangerousCore.JournalEvents;

namespace EDDiscovery.UserControls
{
    public static class ScanDisplayForm
    {
        // tag can be a Isystem or an He.. output depends on it.
        public static async void ShowScanOrMarketForm(Form parent, Object tag, HistoryList hl, float opacity = 1, Color? keycolour = null, WebExternalDataLookup? forcedlookup = null)     
        {
            if (tag == null)
                return;

            ExtendedControls.ConfigurableForm form = new ExtendedControls.ConfigurableForm();

            Size infosize = parent.SizeWithinScreen(new Size(parent.Width * 6 / 8, parent.Height * 6 / 8), 128, 128 + 100);        // go for this, but allow this around window
            int topmargin = 28+28;

            HistoryEntry he = tag as HistoryEntry;                          // is tag HE?
            ISystem sys = he != null ? he.System : tag as ISystem;          // if so, sys is he.system, else its a direct sys
            string title = "System".T(EDTx.ScanDisplayForm_Sys) + ": " + sys.Name;

            AutoScaleMode asm = AutoScaleMode.Font;

            if (he != null && (he.EntryType == JournalTypeEnum.Market || he.EntryType == JournalTypeEnum.EDDCommodityPrices))  // station data..
            {
                he.FillInformation(out string info, out string detailed);

                form.Add(new ExtendedControls.ConfigurableForm.Entry("RTB", typeof(ExtendedControls.ExtRichTextBox), detailed, new Point(0, topmargin), infosize, null));

                JournalCommodityPricesBase jm = he.journalEntry as JournalCommodityPricesBase;
                title += ", " +"Station".T(EDTx.ScanDisplayForm_Station) + ": " + jm.Station;
            }
            else
            {
                StarScan.SystemNode nodedata = null;
                DBSettingsSaver db = new DBSettingsSaver();
                EDSMSpanshButton edsmSpanshButton = new EDSMSpanshButton();
                ScanDisplayBodyFiltersButton filterbut = new ScanDisplayBodyFiltersButton();
                ScanDisplayConfigureButton configbut = new ScanDisplayConfigureButton();
                ScanDisplayUserControl sd = new ScanDisplayUserControl();

                if (forcedlookup == null)   // if we not forced into the mode
                {
                    edsmSpanshButton.Init(db, "EDSMSpansh", "");
                    edsmSpanshButton.ValueChanged += (s, e) =>
                    {
                        nodedata = hl.StarScan.FindSystemSynchronous(sys, edsmSpanshButton.WebLookup);    // look up system, unfort must be sync due to limitations in c#
                        sd.SystemDisplay.ShowWebBodies = edsmSpanshButton.WebLookup != WebExternalDataLookup.None;
                        sd.DrawSystem(nodedata, null, hl.MaterialCommoditiesMicroResources.GetLast(), filter: filterbut.BodyFilters);
                    };
                }

                sd.SystemDisplay.ShowWebBodies = (forcedlookup.HasValue ? forcedlookup.Value : edsmSpanshButton.WebLookup) != WebExternalDataLookup.None;
                int selsize = (int)(ExtendedControls.Theme.Current.GetFont.Height / 10.0f * 48.0f);
                sd.SystemDisplay.SetSize( selsize );
                sd.Size = infosize;

                nodedata = await hl.StarScan.FindSystemAsync(sys, forcedlookup.HasValue ? forcedlookup.Value : edsmSpanshButton.WebLookup);    // look up system async

                //if (data != null) // can't do right now as value changes if edsm button is there, may fix later tbd
                //{
                //    long value = data.ScanValue(lookup != WebExternalDataLookup.None);
                //    title += " ~ " + value.ToString("N0") + " cr";
                //}

                filterbut.Init(db, "BodyFilter");
                filterbut.Image = EDDiscovery.Icons.Controls.EventFilter;
                filterbut.ValueChanged += (s, e) =>
                {
                    sd.DrawSystem(nodedata, null, hl.MaterialCommoditiesMicroResources.GetLast(), filter: filterbut.BodyFilters);
                };

                configbut.Init(db, "DisplayFilter");
                configbut.Image = EDDiscovery.Icons.Controls.DisplayFilters;
                configbut.ValueChanged += (s, e) =>
                {
                    configbut.ApplyDisplayFilters(sd);
                    sd.DrawSystem(nodedata, null, hl.MaterialCommoditiesMicroResources.GetLast(), filter: filterbut.BodyFilters);
                };

                sd.BackColor = ExtendedControls.Theme.Current.Form;
                sd.DrawSystem(nodedata, null, hl.MaterialCommoditiesMicroResources.GetLast(), filter: filterbut.BodyFilters);

                asm = AutoScaleMode.None;   // because we are using a picture box, it does not autoscale, so we can't use that logic on it.

                form.Add(new ExtendedControls.ConfigurableForm.Entry("Body", null, null, new Point(4, 28), new Size(28, 28), null) { control = filterbut });
                form.Add(new ExtendedControls.ConfigurableForm.Entry("Con", null, null, new Point(4 + 28 + 8, 28), new Size(28, 28), null) { control = configbut });
                if ( !forcedlookup.HasValue)
                    form.Add(new ExtendedControls.ConfigurableForm.Entry("edsm", null, null, new Point(4 + 28 + 8 + 28 + 8, 28), new Size(28, 28), null) { control = edsmSpanshButton });
                form.Add(new ExtendedControls.ConfigurableForm.Entry("Sys", null, null, new Point(0, topmargin), infosize, null) { control = sd });
                form.AllowResize = true;
            }

            form.AddOK(new Point(infosize.Width - 120, topmargin + infosize.Height + 10));

            form.Trigger += (dialogname, controlname, ttag) =>
            {
                if (controlname == "OK")
                    form.ReturnResult(DialogResult.OK);
                else if (controlname == "Close")
                    form.ReturnResult(DialogResult.Cancel);
            };

            form.InitCentred( parent, parent.Icon, title, null, null, asm , closeicon:true);

            if (opacity < 1)
            {
                form.Opacity = opacity;
                form.BackColor = keycolour.Value;
                form.TransparencyKey = keycolour.Value;
            }

            form.Show(parent);
        }

        // class needed for buttons for save/restore - global for all instances
        public class DBSettingsSaver : EliteDangerousCore.DB.IUserDatabaseSettingsSaver
        {
            const string root = "ScanDisplayFormCommon_";
            public T GetSetting<T>(string key, T defaultvalue)
            {
                return EliteDangerousCore.DB.UserDatabase.Instance.GetSetting(root + key, defaultvalue);
            }

            public bool PutSetting<T>(string key, T value)
            {
                return EliteDangerousCore.DB.UserDatabase.Instance.PutSetting(root + key, value);
            }
        }
    }
}
