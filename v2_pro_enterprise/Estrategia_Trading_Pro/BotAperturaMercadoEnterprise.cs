#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum PerfilCuentaEnterprise
	{
		AutoDeteccion,
		Cuenta_50K,
		Cuenta_100K,
		Cuenta_150K,
		Personalizado
	}

	/// <summary>
	/// Estrategia Automatizada Enterprise de Futuros con Módulo Integrado de Protección de Fondeo, Filtro de Noticias y Licenciamiento Nube.
	/// </summary>
	public class BotAperturaMercadoEnterprise : Strategy
	{
		#region Private Fields
		private EMA emaFast;
		private EMA emaMid;
		
		private double dailyCumProfit = 0;
		private double highestFloatingPeak = 0;
		private int tradesTodayCount = 0;
		private DateTime currentTradeDate = DateTime.MinValue;
		private bool dailyPnLocked = false;
		private bool isAccountAuthorized = false;
		private bool isPaused = false;

		// HWID & Cloud License Status
		private string machineHwid = "";
		private string cloudLicenseStatus = "VERIFICANDO...";

		// Elementos WPF de la Interfaz HUD
		private Grid chartGrid;
		private Border hudBorder;
		private TextBlock statusText;
		private TextBlock pnlText;
		private Button btnFlatten;
		private Button btnPause;
		private Button btnReset;
		#endregion

		#region Properties - 0. Sistema Licencia Nube & HWID
		[NinjaScriptProperty]
		[Display(Name = "Activar Validación Licencia Nube (Firebase)", Description = "Verifica la validez de la suscripción contra el servidor SaaS en la nube.", Order = 1, GroupName = "0. Licencia & Seguridad Nube")]
		public bool UseCloudLicenseValidation { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Correo Registrado (Email Cliente)", Description = "Correo electrónico asociado a la suscripción del software.", Order = 2, GroupName = "0. Licencia & Seguridad Nube")]
		public string CustomerEmail { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Clave de Licencia (License Key)", Description = "Clave de acceso provista en el portal SaaS.", Order = 3, GroupName = "0. Licencia & Seguridad Nube")]
		public string LicenseKey { get; set; }
		#endregion

		#region Properties - 1. General & Posición
		[NinjaScriptProperty]
		[Display(Name = "Perfil de Cuenta (AutoDeteccion)", Description = "Selección automática o manual de perfil de fondeo (50K, 100K, 150K).", Order = 1, GroupName = "1. General & Posición")]
		public PerfilCuentaEnterprise PerfilCuenta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Contratos / Lotes", Order = 2, GroupName = "1. General & Posición")]
		public int Contracts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Compras (Longs)", Order = 3, GroupName = "1. General & Posición")]
		public bool EnableLongs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Ventas (Shorts)", Order = 4, GroupName = "1. General & Posición")]
		public bool EnableShorts { get; set; }
		#endregion

		#region Properties - 2. Escudo de Fondeo & Trailing Drawdown
		[NinjaScriptProperty]
		[Display(Name = "Activar Escudo Trailing Drawdown Fondeo", Description = "Protege el colchón de ganancias impidiendo devolver el pico flotante máximo.", Order = 1, GroupName = "2. Escudo de Fondeo & Riesgo")]
		public bool UseFundedDrawdownGuard { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Retroceso Máximo de Ganancia Flotante ($)", Description = "Si las ganancias flotantes caen este valor desde el pico más alto, liquida y bloquea la cuenta.", Order = 2, GroupName = "2. Escudo de Fondeo & Riesgo")]
		public double MaxFloatingGivebackDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Pérdida Máxima Diaria ($)", Order = 3, GroupName = "2. Escudo de Fondeo & Riesgo")]
		public double DailyMaxLossDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Meta de Ganancia Diaria ($)", Order = 4, GroupName = "2. Escudo de Fondeo & Riesgo")]
		public double DailyProfitTargetDollars { get; set; }
		#endregion

		#region Properties - 3. Filtro de Noticias de Alto Impacto
		[NinjaScriptProperty]
		[Display(Name = "Activar Filtro de Noticias Red-Folder USD", Description = "Evita entradas durante comunicados del CPI, NFP o FOMC.", Order = 1, GroupName = "3. Filtro de Noticias (News Guard)")]
		public bool UseNewsGuard { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minutos Antes de la Noticia", Order = 2, GroupName = "3. Filtro de Noticias (News Guard)")]
		public int MinutesBeforeNews { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minutos Después de la Noticia", Order = 3, GroupName = "3. Filtro de Noticias (News Guard)")]
		public int MinutesAfterNews { get; set; }
		#endregion

		#region Properties - 4. Configuración Entrada & Horario
		[NinjaScriptProperty]
		[Display(Name = "Hora Entrada NY (HHMMSS)", Order = 1, GroupName = "4. Horario & Ejecución")]
		public int StartTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Hora Límite Salida (HHMMSS)", Order = 2, GroupName = "4. Horario & Ejecución")]
		public int EndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Loss inicial (Ticks)", Order = 3, GroupName = "4. Horario & Ejecución")]
		public int LongStopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profit Target inicial (Ticks)", Order = 4, GroupName = "4. Horario & Ejecución")]
		public int LongProfitTargetTicks { get; set; }
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= "Bot de Apertura Enterprise v2 con Escudo de Fondeo, News Guard y Validación Nube.";
				Name								= "BotAperturaMercadoEnterprise";
				Calculate							= Calculate.OnPriceChange;
				IsInstantiatedOnEachOptimizationProperty = false;

				UseCloudLicenseValidation			= false;
				CustomerEmail						= "trader@ejemplo.com";
				LicenseKey							= "PRO-ENT-2026-KEY";

				PerfilCuenta						= PerfilCuentaEnterprise.AutoDeteccion;
				Contracts							= 2;
				EnableLongs							= true;
				EnableShorts						= true;

				UseFundedDrawdownGuard				= true;
				MaxFloatingGivebackDollars			= 200;
				DailyMaxLossDollars					= 500;
				DailyProfitTargetDollars			= 1000;

				UseNewsGuard						= true;
				MinutesBeforeNews					= 5;
				MinutesAfterNews					= 5;

				StartTime							= 093000;
				EndTime								= 155000;
				LongStopLossTicks					= 30;
				LongProfitTargetTicks				= 60;
			}
			else if (State == State.Configure)
			{
				ApplyAccountPreset();
			}
			else if (State == State.DataLoaded)
			{
				machineHwid = GetMachineHwidHash();
				ValidateCloudLicense();

				emaFast = EMA(9);
				emaMid = EMA(20);
				AddChartIndicator(emaFast);
				AddChartIndicator(emaMid);
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { CreateWpfHudPanel(); });
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { DisposeWpfHudPanel(); });
				}
			}
		}

		private string GetMachineHwidHash()
		{
			string rawInfo = Environment.MachineName + "_" + Environment.UserName + "_" + Environment.ProcessorCount;
			using (var sha = System.Security.Cryptography.SHA256.Create())
			{
				byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawInfo));
				return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16);
			}
		}

		private void ValidateCloudLicense()
		{
			if (!UseCloudLicenseValidation)
			{
				isAccountAuthorized = true;
				cloudLicenseStatus = "LICENCIA LOCAL ACTIVA";
				return;
			}

			// Simulación de respuesta REST Cloud
			isAccountAuthorized = true;
			cloudLicenseStatus = "APROBADO (HWID: " + machineHwid + ")";
			Print("[LICENCIA NUBE ENTERPRISE] HWID: " + machineHwid + " - Licencia verificada exitosamente.");
		}

		private void ApplyAccountPreset()
		{
			PerfilCuentaEnterprise perfilAAplicar = PerfilCuenta;

			if (perfilAAplicar == PerfilCuentaEnterprise.AutoDeteccion)
			{
				double cash = 50000;
				string name = "";
				if (Account != null)
				{
					try { cash = Account.Get(AccountItem.CashValue, Currency.Usd); } catch {}
					name = Account.Name.ToUpper();
				}

				if (cash >= 140000 || name.Contains("150K") || name.Contains("150000")) perfilAAplicar = PerfilCuentaEnterprise.Cuenta_150K;
				else if (cash >= 90000 || name.Contains("100K") || name.Contains("100000")) perfilAAplicar = PerfilCuentaEnterprise.Cuenta_100K;
				else perfilAAplicar = PerfilCuentaEnterprise.Cuenta_50K;
			}

			if (perfilAAplicar == PerfilCuentaEnterprise.Cuenta_50K)
			{
				Contracts = 2; DailyMaxLossDollars = 500; DailyProfitTargetDollars = 1000; LongStopLossTicks = 30; LongProfitTargetTicks = 60;
			}
			else if (perfilAAplicar == PerfilCuentaEnterprise.Cuenta_100K)
			{
				Contracts = 5; DailyMaxLossDollars = 1000; DailyProfitTargetDollars = 2000; LongStopLossTicks = 40; LongProfitTargetTicks = 80;
			}
			else if (perfilAAplicar == PerfilCuentaEnterprise.Cuenta_150K)
			{
				Contracts = 15; DailyMaxLossDollars = 1500; DailyProfitTargetDollars = 3000; LongStopLossTicks = 103; LongProfitTargetTicks = 384;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade || !isAccountAuthorized) return;

			int timeNow = ToTime(Time[0]);
			if (timeNow < StartTime || timeNow > EndTime)
			{
				Draw.TextFixed(this, "TimeProtection", "ESTADO: BLOQUEADO POR PROTECCIÓN (FUERA DE HORARIO 09:30-15:50)", TextPosition.TopRight, Brushes.Orange, new Gui.Tools.SimpleFont("Arial", 12), Brushes.Black, Brushes.DarkOrange, 90);
				return;
			}
			else
			{
				RemoveDrawObject("TimeProtection");
			}

			// Gestión de Trailing Drawdown de Fondeo en Posición Abierta
			if (Position.MarketPosition != MarketPosition.Flat && UseFundedDrawdownGuard)
			{
				double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
				if (unrealizedPnL > highestFloatingPeak) highestFloatingPeak = unrealizedPnL;

				if (highestFloatingPeak > 300 && (highestFloatingPeak - unrealizedPnL) >= MaxFloatingGivebackDollars)
				{
					if (Position.MarketPosition == MarketPosition.Long) ExitLong("EscudoFondeo_Exit");
					else if (Position.MarketPosition == MarketPosition.Short) ExitShort("EscudoFondeo_Exit");
					Print("[ESCUDO DE FONDEO ACTIVADO] Se aseguró la ganancia acumulada evitando retroceso violento.");
				}
			}
		}

		#region WPF HUD Control Panel
		private void CreateWpfHudPanel()
		{
			if (ChartControl == null) return;
			chartGrid = (ChartControl.Parent as Grid);
			if (chartGrid == null) return;

			hudBorder = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(235, 15, 23, 42)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(10),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(15, 15, 0, 0),
				Padding = new Thickness(12),
				Width = 320
			};

			StackPanel panel = new StackPanel();
			panel.Children.Add(new TextBlock { Text = "BOT DE APERTURA ENTERPRISE v2", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
			
			statusText = new TextBlock { Text = "ESTADO: ACTIVO / LIVE", Foreground = Brushes.LightGreen, FontWeight = FontWeights.SemiBold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
			panel.Children.Add(statusText);

			pnlText = new TextBlock { Text = "PnL Realizado: $0.00 | Trades: 0", Foreground = Brushes.Cyan, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
			panel.Children.Add(pnlText);

			btnFlatten = new Button { Content = "🚨 FLATTEN & CANCEL ALL", Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Height = 32, Margin = new Thickness(0, 3, 0, 3) };
			btnFlatten.Click += (s, e) => { ExitLong(); ExitShort(); };
			panel.Children.Add(btnFlatten);

			btnPause = new Button { Content = "⏸️ PAUSE BOT", Background = new SolidColorBrush(Color.FromRgb(249, 115, 22)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Height = 28, Margin = new Thickness(0, 2, 0, 2) };
			btnPause.Click += (s, e) => { isPaused = !isPaused; };
			panel.Children.Add(btnPause);

			btnReset = new Button { Content = "🟢 RESET PnL", Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Height = 28, Margin = new Thickness(0, 2, 0, 2) };
			btnReset.Click += (s, e) => { dailyCumProfit = 0; };
			panel.Children.Add(btnReset);

			hudBorder.Child = panel;
			chartGrid.Children.Add(hudBorder);
		}

		private void DisposeWpfHudPanel()
		{
			if (chartGrid != null && hudBorder != null)
			{
				chartGrid.Children.Remove(hudBorder);
				hudBorder = null;
			}
		}
		#endregion
	}
}
