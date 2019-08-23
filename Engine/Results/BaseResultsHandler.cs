/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Statistics;

namespace QuantConnect.Lean.Engine.Results
{
    /// <summary>
    /// Provides base functionality to the implementations of <see cref="IResultHandler"/>
    /// </summary>
    public abstract class BaseResultsHandler
    {
        /// <summary>
        /// Lock to be used when accessing the chart collection
        /// </summary>
        protected object ChartLock { get; }

        /// <summary>
        /// The algorithm unique compilation id
        /// </summary>
        protected string CompileId { get; set; }

        /// <summary>
        /// The algorithm job id.
        /// This is the deploy id for live, backtesting id for backtesting
        /// </summary>
        protected string JobId { get; set; }

        /// <summary>
        /// The result handler start time
        /// </summary>
        protected DateTime StartTime { get; }

        /// <summary>
        /// Customizable dynamic statistics <see cref="IAlgorithm.RuntimeStatistics"/>
        /// </summary>
        protected Dictionary<string, string> RuntimeStatistics { get; }

        /// <summary>
        /// The handler responsible for communicating messages to listeners
        /// </summary>
        protected IMessagingHandler MessagingHandler;

        /// <summary>
        /// The transaction handler used to get the algorithms Orders information
        /// </summary>
        protected ITransactionHandler TransactionHandler;

        /// <summary>
        /// The algorithms starting portfolio value.
        /// Used to calculate the portfolio return
        /// </summary>
        protected decimal StartingPortfolioValue { get; set; }

        /// <summary>
        /// The algorithm instance
        /// </summary>
        protected IAlgorithm Algorithm { get; set; }

        /// <summary>
        /// The data manager, used to access current subscriptions
        /// </summary>
        protected IDataFeedSubscriptionManager DataManager;

        /// <summary>
        /// Gets or sets the current alpha runtime statistics
        /// </summary>
        protected AlphaRuntimeStatistics AlphaRuntimeStatistics { get; set; }

        /// <summary>
        /// Creates a new instance
        /// </summary>
        protected BaseResultsHandler()
        {
            RuntimeStatistics = new Dictionary<string, string>();
            StartTime = DateTime.UtcNow;
            CompileId = "";
            JobId = "";
            ChartLock = new object();
        }

        /// <summary>
        /// Returns the location of the logs
        /// </summary>
        /// <param name="id">Id that will be incorporated into the algorithm log name</param>
        /// <param name="logs">The logs to save</param>
        /// <returns>The path to the logs</returns>
        public virtual string SaveLogs(string id, IEnumerable<string> logs)
        {
            var path = $"{id}-log.txt";
            File.WriteAllLines(path, logs);
            return Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        /// <summary>
        /// Save the results to disk
        /// </summary>
        /// <param name="name">The name of the results</param>
        /// <param name="result">The results to save</param>
        public virtual void SaveResults(string name, Result result)
        {
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), name), JsonConvert.SerializeObject(result, Formatting.Indented));
        }

        /// <summary>
        /// Sets the current alpha runtime statistics
        /// </summary>
        /// <param name="statistics">The current alpha runtime statistics</param>
        public virtual void SetAlphaRuntimeStatistics(AlphaRuntimeStatistics statistics)
        {
            AlphaRuntimeStatistics = statistics;
        }

        /// <summary>
        /// Sets the current Data Manager instance
        /// </summary>
        public virtual void SetDataManager(IDataFeedSubscriptionManager dataManager)
        {
            DataManager = dataManager;
        }

        /// <summary>
        /// Will generate the statistics results
        /// </summary>
        /// <param name="charts">The complete collection of charts</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="startingPortfolioValue">The starting portfolio value</param>
        /// <param name="banner">Runtime statistics banner information</param>
        protected static StatisticsResults GetStatisticsResult(Dictionary<string, Chart> charts,
            IAlgorithm algorithm,
            decimal startingPortfolioValue,
            Dictionary<string, string> banner)
        {
            var statisticsResults = new StatisticsResults();
            try
            {
                //Generates error when things don't exist (no charting logged, runtime errors in main algo execution)
                // make sure we've taken samples for these series before just blindly requesting them
                if (charts.ContainsKey(Chart.StrategyEquity) &&
                    charts[Chart.StrategyEquity].Series.ContainsKey(Series.Equity) &&
                    charts[Chart.StrategyEquity].Series.ContainsKey(Series.DailyPerformance) &&
                    charts.ContainsKey(Chart.Benchmark) &&
                    charts[Chart.Benchmark].Series.ContainsKey(Series.Benchmark))
                {
                    var equity = charts[Chart.StrategyEquity].Series[Series.Equity].Values;
                    var performance = charts[Chart.StrategyEquity].Series[Series.DailyPerformance].Values;
                    var profitLoss = new SortedDictionary<DateTime, decimal>(algorithm.Transactions.TransactionRecord);
                    var totalTransactions = algorithm.Transactions.GetOrders(x => x.Status.IsFill()).Count();
                    var benchmark = charts[Chart.Benchmark].Series[Series.Benchmark].Values;

                    statisticsResults = StatisticsBuilder.Generate(algorithm.TradeBuilder.ClosedTrades, profitLoss, equity, performance, benchmark,
                        startingPortfolioValue, algorithm.Portfolio.TotalFees, totalTransactions);

                    //Some users have $0 in their brokerage account / starting cash of $0. Prevent divide by zero errors
                    var netReturn = startingPortfolioValue > 0 ?
                                    (algorithm.Portfolio.TotalPortfolioValue - startingPortfolioValue) / startingPortfolioValue
                                    : 0;

                    //Add other fixed parameters.
                    banner["Unrealized"] = "$" + algorithm.Portfolio.TotalUnrealizedProfit.ToString("N2");
                    banner["Fees"] = "-$" + algorithm.Portfolio.TotalFees.ToString("N2");
                    banner["Net Profit"] = "$" + algorithm.Portfolio.TotalProfit.ToString("N2");
                    banner["Return"] = netReturn.ToString("P");
                    banner["Equity"] = "$" + algorithm.Portfolio.TotalPortfolioValue.ToString("N2");
                }
            }
            catch (Exception err)
            {
                Log.Error(err, "BaseResultsHandler(): Error generating statistics packet");
            }

            return statisticsResults;
        }
    }
}
