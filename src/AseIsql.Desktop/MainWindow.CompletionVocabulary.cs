namespace AseIsql.Desktop;

public partial class MainWindow
{
    // SAP ASE Reference Manual: Tables, 16.0 SP04 and 16.1 (system tables/views and MDA tables).
    // https://help.sap.com/doc/a612d2d0bc2b1014a731f1be020d1d58/16.1.0.2/en-US/SAP_ASE_Reference_Manual_Tables_en.pdf
    // This is a local vocabulary, not a claim that an object exists on the connected server.
    // mon_dbs is explicitly user-requested compatibility vocabulary, not an ASE built-in from that manual.
    private static readonly string[] SqlCompletionSystemObjects = ("""
        ch_events sysdams syblicenseslog sysalternates sysaltusages sysattributes sysauditoptions
        sysaudits_01 sysaudits_02 sysaudits_03 sysaudits_04 sysaudits_05 sysaudits_06 sysaudits_07 sysaudits_08
        syscacheinfo syscachepoolinfo syscertificatestore syscharsets sysjwtproviders syscolumns syscomments
        sysconfigures sysconstraints syscoordinations syscurconfigs sysdatabases sysdepends sysdevices
        sysencryptkeys sysengines sysgams sysimrslogs sysindexes sysinstances sysjars syskeys syslanguages
        syslisteners syslocks syslogininfo sysloginroles syslogins syslogs syslogshold sysmessages sysmonitors
        sysobjects sysoptions syspartitionkeys syspartitions syspoolinfo sysprocedures sysprocesses sysprotects
        sysquerymetrics sysqueryplans sysreferences sysremotelogins sysresourcelimits sysroles syssecmechs
        syssegments sysservers syssequences syssessions sysslices syssrvroles sysstatistics systabstats
        systhresholds systimeranges systransactions systypes sysusages sysusermessages sysusers sysversions sysxtypes
        monBackupserverErrorlog monBucketPool monCachedObject monCachePool monCachedProcedures monCachedStatement
        monCIPC monCIPCEndpoints monCIPCLinks monCIPCMesh monCLMObjectActivity monClusterCacheManager monCMSFailover
        monDataCache monDBRecovery monDBRecoveryLRTypes monDeadLock monDeviceIO monDeviceSegmentIO monDeviceSpaceUsage
        monDeviceSegmentUsage monEngine monErrorLog monFailoverRecovery monHADRMembers monHANANonPushdown
        monHCBGCTasks monHCBPartitionActivity monHCBTuningActivity monIMRSCache monIMRSCacheActivity monIMRSGCTasks
        monImrslogRecovery monIMRSPartitionActivity monInmemoryStorage monIOController monIOQueue monLatchFreeIndex
        monLicense monLocks monLockTimeout monLogicalCluster monLogicalClusterAction monLogicalClusterInstance
        monLogicalClusterRoute monMemoryDumps monMemoryUsage monNetworkIO monNVCache monOpenDatabases monOpenObjectActivity
        monOpenPartitionActivity monPCIBridge monPCIEngine monPCISlots monPCM monProcedureCache monProcedureCacheMemoryUsage
        monProcedureCacheModuleUsage monProcELC monProcess monProcessActivity monProcessLookup monProcessMigration
        monProcessNetIO monProcessObject monProcessProcedures monProcessSQLText monProcessStatement monProcessWaits
        monProcessWorkerThread monRepCoordinator monRepLogActivity monRepMemoryStatistics monRepScanners
        monRepScannersTotalTime monRepSchemaCache monRepSenders monRepStreamStatistics monRepSyncTaskStatistics
        monRepTruncationPoint monSpinlockActivity monSQLRepActivity monSQLRepMisses monSSLCertInfo monState
        monStatementCache monSysExecutionTime monSysLoad monSysPlanText monSysSQLText monSysStatement monSysWaits
        monSysWorkerThread monTableColumns monTableCompression monTableParameters monTables monTableTransfer monTask
        monTempdbActivity monThread monThreadPool monThresholdEvent monWaitClassInfo monWaitEventInfo monWorkload
        monWorkloadPreview monWorkloadProfile monWorkloadRaw monWorkQueue mon_dbs
        """).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
