#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EmergencyFlattenTool : Strategy
    {
        private bool hasExecuted = false;

        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    Description = "HERRAMIENTA DE EMERGENCIA PARA LIQUIDAR Y CANCELAR TODO EN TODAS LAS CUENTAS";
                    Name        = "EmergencyFlattenTool";
                    Calculate   = Calculate.OnPriceChange;
                    IsInstantiatedOnEachOptimizationIteration = false;
                }
                else if (State == State.DataLoaded || State == State.Realtime)
                {
                    if (!hasExecuted)
                    {
                        hasExecuted = true;
                        RMF_EjecutarLiquidadorDeEmergencia();
                    }
                }
            }
            catch (Exception ex)
            {
                Print("[EMERGENCY TOOL ERROR] " + ex.Message);
            }
        }

        protected override void OnBarUpdate()
        {
            if (!hasExecuted)
            {
                hasExecuted = true;
                RMF_EjecutarLiquidadorDeEmergencia();
            }
        }

        private void RMF_EjecutarLiquidadorDeEmergencia()
        {
            try
            {
                if (Account.All == null) return;

                lock (Account.All)
                {
                    foreach (Account acc in Account.All)
                    {
                        if (acc == null) continue;
                        try
                        {
                            // 1. Cancelar todas las ordenes pendientes de forma segura
                            if (acc.Orders != null)
                            {
                                List<Order> workingOrders = new List<Order>();
                                foreach (Order o in acc.Orders)
                                {
                                    if (o != null && (o.OrderState == OrderState.Working ||
                                                      o.OrderState == OrderState.Submitted ||
                                                      o.OrderState == OrderState.Accepted))
                                    {
                                        workingOrders.Add(o);
                                    }
                                }

                                if (workingOrders.Count > 0)
                                {
                                    acc.Cancel(workingOrders.ToArray());
                                    Print("[EMERGENCY TOOL] Canceladas " + workingOrders.Count + " ordenes en: " + acc.Name);
                                }
                            }

                            // 2. Liquidar de forma nativa enviando BuyToCover para posiciones Short y Sell para posiciones Long
                            if (acc.Positions != null)
                            {
                                List<Instrument> activeInstruments = new List<Instrument>();
                                foreach (Position pos in acc.Positions)
                                {
                                    if (pos != null && pos.MarketPosition != MarketPosition.Flat && pos.Quantity > 0)
                                    {
                                        activeInstruments.Add(pos.Instrument);
                                    }
                                }

                                if (activeInstruments.Count > 0)
                                {
                                    acc.Flatten(activeInstruments.ToArray());
                                    Print("[EMERGENCY FLATTEN NATIVO] Liquidadas posiciones activas en " + acc.Name);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Print("[EMERGENCY ERROR CUENTA " + acc.Name + "] " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print("[EMERGENCY FLATTEN GENERAL ERROR] " + ex.Message);
            }
        }
    }
}
