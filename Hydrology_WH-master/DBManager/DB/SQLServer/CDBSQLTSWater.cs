﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Hydrology.DBManager.Interface;
using Hydrology.Entity;


namespace Hydrology.DBManager.DB.SQLServer
{
    public class CDBSQLTSWater : CSQLBase, ITSWater
    {
        #region 静态常量
        private const string CT_EntityName = "CEntityTSWater";   //  数据库表Water实体类
        public static readonly string CT_TableName = "TSwater";      //数据库中水量表的名字
        public static readonly string CN_StationId = "stationid"; //站点ID
        public static readonly string CN_DataTime = "datatime";    //数据的采集时间
        public static readonly string CN_WaterStage = "waterstage";  //水位
        public static readonly string CN_TransType = "transtype";  //通讯方式
        public static readonly string CN_MsgType = "messagetype";  //报送类型
        public static readonly string CN_RecvDataTime = "recvdatatime";    //接收到数据的时间
        #endregion
        private string m_strStaionId;       //需要查询的测站
        private DateTime m_startTime;  //查询起始时间
        private DateTime m_endTime;    //查询结束时间
        private bool m_TimeSelect;
        private string TimeSelectString
        {
            get
            {
                if (m_TimeSelect == false)
                {
                    return "";
                }
                else
                {
                    return "convert(VARCHAR," + CN_DataTime + ",120) LIKE '%00:00%' and ";
                }
            }
        }
        public CDBSQLTSWater()
            : base()
        {
            m_tableDataAdded.Columns.Add(CN_StationId);
            m_tableDataAdded.Columns.Add(CN_DataTime);
            m_tableDataAdded.Columns.Add(CN_RecvDataTime);
            m_tableDataAdded.Columns.Add(CN_WaterStage);
            m_tableDataAdded.Columns.Add(CN_TransType);
            m_tableDataAdded.Columns.Add(CN_MsgType);
            // 初始化互斥量
            m_mutexWriteToDB = CDBMutex.Mutex_TB_Water;
        }

        public void AddNewRow(CEntityTSWater water)
        {
            // 记录超过1000条，或者时间超过1分钟，就将当前的数据写入数据库
            m_mutexDataTable.WaitOne(); //等待互斥量
            DataRow row = m_tableDataAdded.NewRow();
            row[CN_StationId] = water.StationID;
            row[CN_DataTime] = water.TimeCollect.ToString(CDBParams.GetInstance().DBDateTimeFormat);
            row[CN_WaterStage] = water.WaterStage;
            row[CN_MsgType] = CEnumHelper.MessageTypeToDBStr(water.MessageType);
            row[CN_TransType] = CEnumHelper.ChannelTypeToDBStr(water.ChannelType);
            row[CN_RecvDataTime] = water.TimeRecieved.ToString(CDBParams.GetInstance().DBDateTimeFormat);
            m_tableDataAdded.Rows.Add(row);
            m_mutexDataTable.ReleaseMutex();
            AddDataToDB(); 
        }

        public void AddNewRows(List<CEntityTSWater> waters)
        {
            m_mutexDataTable.WaitOne(); //等待互斥量
            foreach (CEntityTSWater water in waters)
            {
                DataRow row = m_tableDataAdded.NewRow();
                row[CN_StationId] = water.StationID;
                row[CN_DataTime] = water.TimeCollect.ToString(CDBParams.GetInstance().DBDateTimeFormat);
                row[CN_WaterStage] = water.WaterStage;
                row[CN_MsgType] = CEnumHelper.MessageTypeToDBStr(water.MessageType);
                row[CN_TransType] = CEnumHelper.ChannelTypeToDBStr(water.ChannelType);
                row[CN_RecvDataTime] = water.TimeRecieved.ToString(CDBParams.GetInstance().DBDateTimeFormat);
                m_tableDataAdded.Rows.Add(row);
            }
            AddDataToDB();
            m_mutexDataTable.ReleaseMutex();
        }

        protected override bool AddDataToDB()
        {
            m_mutexDataTable.WaitOne();
            if (m_tableDataAdded.Rows.Count <= 0)
            {
                m_mutexDataTable.ReleaseMutex();
                return true;
            }
            //清空内存表的所有内容，把内容复制到临时表tmp中
            DataTable tmp = m_tableDataAdded.Copy();
            m_tableDataAdded.Rows.Clear();

            // 释放内存表的互斥量
            m_mutexDataTable.ReleaseMutex();

            // 先获取对数据库的唯一访问权
            m_mutexWriteToDB.WaitOne();
            try
            {
                //将临时表中的内容写入数据库
                //SqlConnection conn = CDBManager.GetInstacne().GetConnection();
                //conn.Open();
                string connstr = CDBManager.Instance.GetConnectionString();
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connstr))
                {
                    bulkCopy.BatchSize = 1;
                    bulkCopy.BulkCopyTimeout = 1800;
                    bulkCopy.DestinationTableName = CT_TableName;
                    bulkCopy.ColumnMappings.Add(CN_StationId, CN_StationId);
                    bulkCopy.ColumnMappings.Add(CN_DataTime, CN_DataTime);
                    bulkCopy.ColumnMappings.Add(CN_WaterStage, CN_WaterStage);
                    bulkCopy.ColumnMappings.Add(CN_TransType, CN_TransType);
                    bulkCopy.ColumnMappings.Add(CN_MsgType, CN_MsgType);
                    bulkCopy.ColumnMappings.Add(CN_RecvDataTime, CN_RecvDataTime);
                    try
                    {
                        bulkCopy.WriteToServer(tmp);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.ToString());
                    }
                }
                //conn.Close();   //关闭连接
                Debug.WriteLine("###{0} :add {1} lines to water db", DateTime.Now, tmp.Rows.Count);
                CDBLog.Instance.AddInfo(string.Format("添加{0}行到调试水位表", tmp.Rows.Count));
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return false;
            }
            finally
            {
                m_mutexWriteToDB.ReleaseMutex();
            }
        }

        public void SetFilter(string stationId, DateTime timeStart, DateTime timeEnd, bool TimeSelect)
        {
            // 设置查询条件
            if (null == m_strStaionId)
            {
                // 第一次查询
                m_iRowCount = -1;
                m_iPageCount = -1;
                m_strStaionId = stationId;
                m_startTime = timeStart;
                m_endTime = timeEnd;
                m_TimeSelect = TimeSelect;
            }
            else
            {
                // 不是第一次查询
                if (stationId != m_strStaionId || timeStart != m_startTime || timeEnd != m_endTime || m_TimeSelect != TimeSelect)
                {
                    m_iRowCount = -1;
                    m_iPageCount = -1;
                    m_mapDataTable.Clear(); //清空上次查询缓存
                }
                m_strStaionId = stationId;
                m_startTime = timeStart;
                m_endTime = timeEnd;
                m_TimeSelect = TimeSelect;
            }
        }

        public List<CEntityTSWater> GetPageData(int pageIndex)
        {
            if (pageIndex <= 0 || m_startTime == null || m_endTime == null || m_strStaionId == null)
            {
                return new List<CEntityTSWater>();
            }
            // 获取某一页的数据，判断所需页面是否在内存中有值
            int startIndex = (pageIndex - 1) * CDBParams.GetInstance().UIPageRowCount + 1;
            int key = (int)(startIndex / CDBParams.GetInstance().DBPageRowCount) + 1; //对应于数据库中的索引
            int startRow = startIndex - (key - 1) * CDBParams.GetInstance().DBPageRowCount - 1;
            Debug.WriteLine("startIndex;{0} key:{1} startRow:{2}", startIndex, key, startRow);
            // 判断MAP中是否有值
            if (m_mapDataTable.ContainsKey(key))
            {
                // 从内存中读取
                return CopyDataToList(key, startRow);
            }
            else
            {
                // 从数据库中查询
                int topcount = key * CDBParams.GetInstance().DBPageRowCount;
                int rowidmim = topcount - CDBParams.GetInstance().DBPageRowCount;
                string sql = " select * from ( " +
                    "select top " + topcount.ToString() + " row_number() over( order by " + CN_DataTime + " ) as " + CN_RowId + ",* " +
                    "from " + CT_TableName + " " +
                    "where " + CN_StationId + "=" + m_strStaionId.ToString() + " " +
                    "and " + TimeSelectString + CN_DataTime + " between " + DateTimeToDBStr(m_startTime) +
                    "and " + DateTimeToDBStr(m_endTime) +
                    ") as tmp1 " +
                    "where " + CN_RowId + ">" + rowidmim.ToString() +
                    " order by " + CN_DataTime;
                SqlDataAdapter adapter = new SqlDataAdapter(sql, CDBManager.GetInstacne().GetConnection());
                DataTable dataTableTmp = new DataTable();
                adapter.Fill(dataTableTmp);
                m_mapDataTable.Add(key, dataTableTmp);
                return CopyDataToList(key, startRow);
            }
        }

        private List<CEntityTSWater> CopyDataToList(int key, int startRow)
        {
            List<CEntityTSWater> result = new List<CEntityTSWater>();
            // 取最小值 ，保证不越界
            int endRow = Math.Min(m_mapDataTable[key].Rows.Count, startRow + CDBParams.GetInstance().UIPageRowCount);
            DataTable table = m_mapDataTable[key];
            for (; startRow < endRow; ++startRow)
            {
                CEntityTSWater water = new CEntityTSWater();
                // water.WaterID = long.Parse(table.Rows[startRow][CN_WaterID].ToString());
                water.StationID = table.Rows[startRow][CN_StationId].ToString();
                water.TimeCollect = DateTime.Parse(table.Rows[startRow][CN_DataTime].ToString());
                //水位
                if (!table.Rows[startRow][CN_WaterStage].ToString().Equals(""))
                {
                    water.WaterStage = Decimal.Parse(table.Rows[startRow][CN_WaterStage].ToString());
                }
                else
                {
                    //11.12
                    water.WaterStage = -9999;
                }
                //流量
                water.TimeRecieved = DateTime.Parse(table.Rows[startRow][CN_RecvDataTime].ToString());
                water.ChannelType = CEnumHelper.DBStrToChannelType(table.Rows[startRow][CN_TransType].ToString());
                water.MessageType = CEnumHelper.DBStrToMessageType(table.Rows[startRow][CN_MsgType].ToString());
                result.Add(water);
            }
            return result;
        }
    }
}
