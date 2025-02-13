﻿/*
 * * Created by Traesh for AshamaneProject (https://github.com/AshamaneProject)
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using WowHeadParser.Entities;
using WOWSharp.Community;

namespace WowHeadParser
{
    class Zone
    {
        const int MAX_WORKER = 20;
        Queue<string> _writeQueue = new Queue<string>();
        Task _parseTask;

        public Zone(MainWindow view, String optionName)
        {
            m_view = view;
            m_zoneId = "0";
            m_index = 0;
            m_parsedEntitiesCount = 0;
            m_getZoneListBackgroundWorker = new BackgroundWorker[MAX_WORKER];

            m_fileName      = "";
            m_optionName    = optionName;
            m_array         = new List<Entity>();
        }

        private void ResetZone()
        {
            if (m_view != null)
                m_view.setProgressBar(0);

            m_index = 0;
            m_array.Clear();
            m_parsedEntitiesCount = 0;
            m_timestamp = Tools.GetUnixTimestamp();
            m_fileName = Tools.GetFileNameForCurrentTime(m_optionName);
            m_timestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

            FileInfo fi = new FileInfo(m_fileName);

            if (!Directory.Exists(fi.DirectoryName))
                Directory.CreateDirectory(fi.DirectoryName);

            if (!File.Exists(m_fileName))
                File.Create(m_fileName);

            if (_parseTask == null)
            {
                _parseTask = new Task(async () =>
                {
                    while (true)
                    {
                        while (_writeQueue.Any())
                        {
                            var requestText = _writeQueue.Dequeue();

                            if (!string.IsNullOrEmpty(requestText))
                                File.AppendAllText(m_fileName, requestText);
                        }

                        await Task.Delay(100);
                    }
                });
                _parseTask.Start();
            }
        }

        public bool StartParsing(String zone)
        {
            Entity askedEntity = m_view.CreateNeededEntity();

            if (askedEntity == null)
                return false;

            ResetZone();
            m_zoneId = zone;

            if (askedEntity.GetType() != typeof(BlackMarket))
                m_zoneHtml = GetZoneHtmlFromWowhead(m_zoneId);

            ParseZoneJson();
            StartSnifByEntity();
            return true;
        }

        public String GetZoneHtmlFromWowhead(String zone)
        {
            return Tools.GetHtmlFromWowhead(Tools.GetWowheadUrl("zone", zone), new System.Net.Http.HttpClient(), new FileCacheManager());
        }

        public void ParseZoneJson()
        {
            Entity askedEntity = m_view.CreateNeededEntity();

            if (askedEntity == null)
                return;

            List<Entity> entityList = m_view.CreateNeededEntity().GetIdsFromZone(m_zoneId, m_zoneHtml);

            if (entityList != null)
            {
                m_count = entityList.Count;
                m_array.AddRange(entityList);
            }
        }

        void StartSnifByEntity()
        {
            m_index = 0;
            m_parsedEntitiesCount = 0;

            int maxWorkers = m_count > MAX_WORKER ? MAX_WORKER : m_count;

            for (int i = 0; i < maxWorkers; ++i)
            {
                m_getZoneListBackgroundWorker[i] = new BackgroundWorker();
                m_getZoneListBackgroundWorker[i].DoWork += new DoWorkEventHandler(BackgroundWorkerProcessEntitiesList);
                m_getZoneListBackgroundWorker[i].RunWorkerCompleted += new RunWorkerCompletedEventHandler(BackgroundWorkerProcessEntitiesCompleted);
                m_getZoneListBackgroundWorker[i].WorkerReportsProgress = true;
                m_getZoneListBackgroundWorker[i].WorkerSupportsCancellation = true;
                m_getZoneListBackgroundWorker[i].RunWorkerAsync(i);
            }
        }

        private void BackgroundWorkerProcessEntitiesList(object sender, DoWorkEventArgs e)
        {
            if (m_index >= m_array.Count)
                return;

            int tempIndex = m_index++;
            try
            {
                e.Result = e.Argument;
                bool parseReturn = m_array[tempIndex].ParseSingleJson();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur");
            }
            ++m_parsedEntitiesCount;
        }

        private void BackgroundWorkerProcessEntitiesCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (m_parsedEntitiesCount > m_array.Count)
                return;

            Console.WriteLine("Affected: " + m_parsedEntitiesCount);

            if (m_view != null)
            {
                EstimateSecondsTimeLeft();
            }

            if (m_parsedEntitiesCount == m_array.Count)
            {
                AppendAllEntitiesToSql();
                m_view.SetWorkDone();
                return;
            }

            if (m_index >= m_array.Count)
                return;

            int workerIndex = (int)e.Result;

            if (!m_getZoneListBackgroundWorker[workerIndex].IsBusy)
                m_getZoneListBackgroundWorker[workerIndex].RunWorkerAsync(workerIndex);
        }

        void AppendAllEntitiesToSql()
        {
            int tempCount = 0;


            foreach (Entity entity in m_array)
            {
                try
                {
                    Console.WriteLine("Doing entity n°" + tempCount++);
                    String requestText = entity.GetSQLRequest();
                    requestText += requestText != "" ? "\n" : "";
                    _writeQueue.Enqueue(requestText);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);
                }
            }

            Console.WriteLine("Elapsed Time : " + ((Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds - m_timestamp));
        }

        private void EstimateSecondsTimeLeft()
        {
            Int32 unixTimestamp = Tools.GetUnixTimestamp();

            if ((m_lastEstimateTime + 1) >= unixTimestamp)
                return;

            m_lastEstimateTime = unixTimestamp;

            float elapsedSeconds = unixTimestamp - m_timestamp;

            float entityCount = m_array.Count();
            float timeByEntity = (float)elapsedSeconds / (float)m_parsedEntitiesCount;

            float estimatedSecondsLeft = timeByEntity * (entityCount - m_parsedEntitiesCount);
            float totalTime = timeByEntity * entityCount;
            float percent = estimatedSecondsLeft / totalTime * 100;

            // percent: actually percent unfinished from 100
            // So percent = 75 would mean we're 25% done
            m_view.setProgressBar(100 - (int)percent);
            m_view.SetTimeLeft((Int32)estimatedSecondsLeft);
        }

        private MainWindow m_view;

        private String m_zoneId;
        private String m_zoneHtml;

        private String m_fileName;
        private String m_optionName;

        private List<Entity> m_array;
        private int m_index;
        private int m_count;
        private int m_parsedEntitiesCount;

        private BackgroundWorker[] m_getZoneListBackgroundWorker;

        private int m_timestamp;
        private int m_lastEstimateTime;
    }
}
