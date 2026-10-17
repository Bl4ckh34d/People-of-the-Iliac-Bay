using System.Collections.Generic;

namespace LivingCities.Core
{
    // Explicit source appearance mappings. Unknown or ungenerated identities have no fallback face.
    public static class SleepArtCatalog
    {
        public sealed class Entry
        {
            public readonly int Archive, Record, Cell;
            public readonly string File;
            internal Entry(int archive, int record, string file, int cell)
            { Archive=archive; Record=record; File=file; Cell=cell; }
        }
        private static readonly Dictionary<int, Entry> entries = Build();
        public static IEnumerable<Entry> Entries { get { return entries.Values; } }
        public static Entry Find(int archive, int record)
        {
            if(archive<0 || archive>999 || record<0 || record>999) return null;
            Entry entry; return entries.TryGetValue(archive*1000+record,out entry)?entry:null;
        }
        public static Entry ForResident(Resident resident)
        {
            if(resident==null) return null;
            if(resident.NativeIdentity!=null) return Find(resident.NativeIdentity.TextureArchive,resident.NativeIdentity.TextureRecord);
            if(resident.AgeGroup==ResidentAgeGroup.Child) return Find(182,resident.Sex==ResidentSex.Female?38:43);
            int variant=(int)((resident.AppearanceSeed>>1)%4);
            // These are DFU MobilePersonBillboard's arrays. Other races display as Breton.
            int[] male, female;
            if(resident.Race==2) { male=new[]{381,382,383,384}; female=new[]{395,396,397,398}; }
            else if(resident.Race==3) { male=new[]{387,388,389,390}; female=new[]{392,393,451,452}; }
            else { male=new[]{385,386,391,394}; female=new[]{453,454,455,456}; }
            return Find((resident.Sex==ResidentSex.Male?male:female)[variant],5);
        }
        private static Dictionary<int,Entry> Build()
        {
            var result=new Dictionary<int,Entry>();
            result.Add(175000,new Entry(175,0,"sleep-topdown-batch-00.png.bytes",0));
            result.Add(175008,new Entry(175,8,"sleep-topdown-batch-00.png.bytes",1));
            result.Add(175010,new Entry(175,10,"sleep-topdown-batch-00.png.bytes",2));
            result.Add(175014,new Entry(175,14,"sleep-topdown-batch-00.png.bytes",3));
            result.Add(175015,new Entry(175,15,"sleep-topdown-batch-00.png.bytes",4));
            result.Add(176000,new Entry(176,0,"sleep-topdown-batch-00.png.bytes",5));
            result.Add(176001,new Entry(176,1,"sleep-topdown-batch-00.png.bytes",6));
            result.Add(176002,new Entry(176,2,"sleep-topdown-batch-00.png.bytes",7));
            result.Add(176003,new Entry(176,3,"sleep-topdown-batch-01.png.bytes",0));
            result.Add(176004,new Entry(176,4,"sleep-topdown-batch-01.png.bytes",1));
            result.Add(176005,new Entry(176,5,"sleep-topdown-batch-01.png.bytes",2));
            result.Add(176006,new Entry(176,6,"sleep-topdown-batch-01.png.bytes",3));
            result.Add(177000,new Entry(177,0,"sleep-topdown-batch-01.png.bytes",4));
            result.Add(177001,new Entry(177,1,"sleep-topdown-batch-01.png.bytes",5));
            result.Add(177002,new Entry(177,2,"sleep-topdown-batch-01.png.bytes",6));
            result.Add(177003,new Entry(177,3,"sleep-topdown-batch-01.png.bytes",7));
            result.Add(177004,new Entry(177,4,"sleep-topdown-batch-02.png.bytes",0));
            result.Add(177005,new Entry(177,5,"sleep-topdown-batch-02.png.bytes",1));
            result.Add(178000,new Entry(178,0,"sleep-topdown-batch-02.png.bytes",2));
            result.Add(178001,new Entry(178,1,"sleep-topdown-batch-02.png.bytes",3));
            result.Add(178002,new Entry(178,2,"sleep-topdown-batch-02.png.bytes",4));
            result.Add(178003,new Entry(178,3,"sleep-topdown-batch-02.png.bytes",5));
            result.Add(178004,new Entry(178,4,"sleep-topdown-batch-02.png.bytes",6));
            result.Add(178005,new Entry(178,5,"sleep-topdown-batch-02.png.bytes",7));
            result.Add(178006,new Entry(178,6,"sleep-topdown-batch-03.png.bytes",0));
            result.Add(179000,new Entry(179,0,"sleep-topdown-batch-03.png.bytes",1));
            result.Add(179001,new Entry(179,1,"sleep-topdown-batch-03.png.bytes",2));
            result.Add(179002,new Entry(179,2,"sleep-topdown-batch-03.png.bytes",3));
            result.Add(179003,new Entry(179,3,"sleep-topdown-batch-03.png.bytes",4));
            result.Add(179004,new Entry(179,4,"sleep-topdown-batch-03.png.bytes",5));
            result.Add(180000,new Entry(180,0,"sleep-topdown-batch-03.png.bytes",6));
            result.Add(180001,new Entry(180,1,"sleep-topdown-batch-03.png.bytes",7));
            result.Add(180002,new Entry(180,2,"sleep-topdown-batch-04.png.bytes",0));
            result.Add(180003,new Entry(180,3,"sleep-topdown-batch-04.png.bytes",1));
            result.Add(181000,new Entry(181,0,"sleep-topdown-batch-04.png.bytes",2));
            result.Add(181001,new Entry(181,1,"sleep-topdown-batch-04.png.bytes",3));
            result.Add(181002,new Entry(181,2,"sleep-topdown-batch-04.png.bytes",4));
            result.Add(181003,new Entry(181,3,"sleep-topdown-batch-04.png.bytes",5));
            result.Add(181004,new Entry(181,4,"sleep-topdown-batch-04.png.bytes",6));
            result.Add(181005,new Entry(181,5,"sleep-topdown-batch-04.png.bytes",7));
            result.Add(181006,new Entry(181,6,"sleep-topdown-batch-05.png.bytes",0));
            result.Add(181007,new Entry(181,7,"sleep-topdown-batch-05.png.bytes",1));
            result.Add(182000,new Entry(182,0,"sleep-topdown-flat-182-0.png.bytes",-1));
            result.Add(182001,new Entry(182,1,"sleep-topdown-batch-05.png.bytes",3));
            result.Add(182002,new Entry(182,2,"sleep-topdown-batch-05.png.bytes",4));
            result.Add(182003,new Entry(182,3,"sleep-topdown-batch-05.png.bytes",5));
            result.Add(182004,new Entry(182,4,"sleep-topdown-batch-05.png.bytes",6));
            result.Add(182005,new Entry(182,5,"sleep-topdown-batch-05.png.bytes",7));
            result.Add(334006,new Entry(334,6,"sleep-topdown-batch-05.png.bytes",7));
            result.Add(346003,new Entry(346,3,"sleep-topdown-batch-05.png.bytes",7));
            result.Add(182006,new Entry(182,6,"sleep-topdown-batch-06.png.bytes",0));
            result.Add(334003,new Entry(334,3,"sleep-topdown-batch-06.png.bytes",0));
            result.Add(182007,new Entry(182,7,"sleep-topdown-batch-06.png.bytes",1));
            result.Add(182008,new Entry(182,8,"sleep-topdown-batch-06.png.bytes",2));
            result.Add(182009,new Entry(182,9,"sleep-topdown-batch-06.png.bytes",3));
            result.Add(357002,new Entry(357,2,"sleep-topdown-batch-06.png.bytes",3));
            result.Add(182010,new Entry(182,10,"sleep-topdown-batch-06.png.bytes",4));
            result.Add(182011,new Entry(182,11,"sleep-topdown-batch-06.png.bytes",5));
            result.Add(182012,new Entry(182,12,"sleep-topdown-batch-06.png.bytes",6));
            result.Add(182013,new Entry(182,13,"sleep-topdown-batch-06.png.bytes",7));
            result.Add(182014,new Entry(182,14,"sleep-topdown-batch-07.png.bytes",0));
            result.Add(182015,new Entry(182,15,"sleep-topdown-batch-07.png.bytes",1));
            result.Add(182016,new Entry(182,16,"sleep-topdown-batch-07.png.bytes",2));
            result.Add(182017,new Entry(182,17,"sleep-topdown-batch-07.png.bytes",3));
            result.Add(182018,new Entry(182,18,"sleep-topdown-batch-07.png.bytes",4));
            result.Add(182019,new Entry(182,19,"sleep-topdown-batch-07.png.bytes",5));
            result.Add(182020,new Entry(182,20,"sleep-topdown-batch-07.png.bytes",6));
            result.Add(182021,new Entry(182,21,"sleep-topdown-batch-07.png.bytes",7));
            result.Add(182022,new Entry(182,22,"sleep-topdown-batch-08.png.bytes",0));
            result.Add(182023,new Entry(182,23,"sleep-topdown-batch-08.png.bytes",1));
            result.Add(182024,new Entry(182,24,"sleep-topdown-batch-08.png.bytes",2));
            result.Add(334007,new Entry(334,7,"sleep-topdown-batch-08.png.bytes",2));
            result.Add(182025,new Entry(182,25,"sleep-topdown-batch-08.png.bytes",3));
            result.Add(182026,new Entry(182,26,"sleep-topdown-batch-08.png.bytes",4));
            result.Add(182027,new Entry(182,27,"sleep-topdown-batch-08.png.bytes",5));
            result.Add(334004,new Entry(334,4,"sleep-topdown-batch-08.png.bytes",5));
            result.Add(182028,new Entry(182,28,"sleep-topdown-batch-08.png.bytes",6));
            result.Add(346010,new Entry(346,10,"sleep-topdown-batch-08.png.bytes",6));
            result.Add(182029,new Entry(182,29,"sleep-topdown-batch-08.png.bytes",7));
            result.Add(182030,new Entry(182,30,"sleep-topdown-batch-09.png.bytes",0));
            result.Add(182031,new Entry(182,31,"sleep-topdown-batch-09.png.bytes",1));
            result.Add(182032,new Entry(182,32,"sleep-topdown-batch-09.png.bytes",2));
            result.Add(182033,new Entry(182,33,"sleep-topdown-batch-09.png.bytes",3));
            result.Add(182034,new Entry(182,34,"sleep-topdown-batch-09.png.bytes",4));
            result.Add(182035,new Entry(182,35,"sleep-topdown-batch-09.png.bytes",5));
            result.Add(357004,new Entry(357,4,"sleep-topdown-batch-09.png.bytes",5));
            result.Add(182036,new Entry(182,36,"sleep-topdown-batch-09.png.bytes",6));
            result.Add(357005,new Entry(357,5,"sleep-topdown-batch-09.png.bytes",6));
            result.Add(182037,new Entry(182,37,"sleep-topdown-batch-09.png.bytes",7));
            result.Add(182038,new Entry(182,38,"sleep-topdown-batch-10.png.bytes",0));
            result.Add(182039,new Entry(182,39,"sleep-topdown-batch-10.png.bytes",1));
            result.Add(182040,new Entry(182,40,"sleep-topdown-batch-10.png.bytes",2));
            result.Add(346014,new Entry(346,14,"sleep-topdown-batch-10.png.bytes",2));
            result.Add(182041,new Entry(182,41,"sleep-topdown-batch-10.png.bytes",3));
            result.Add(182042,new Entry(182,42,"sleep-topdown-batch-10.png.bytes",4));
            result.Add(334009,new Entry(334,9,"sleep-topdown-batch-10.png.bytes",4));
            result.Add(182043,new Entry(182,43,"sleep-topdown-batch-10.png.bytes",5));
            result.Add(182044,new Entry(182,44,"sleep-topdown-batch-10.png.bytes",6));
            result.Add(182045,new Entry(182,45,"sleep-topdown-batch-10.png.bytes",7));
            result.Add(334010,new Entry(334,10,"sleep-topdown-batch-10.png.bytes",7));
            result.Add(182046,new Entry(182,46,"sleep-topdown-batch-11.png.bytes",0));
            result.Add(182047,new Entry(182,47,"sleep-topdown-batch-11.png.bytes",1));
            result.Add(182048,new Entry(182,48,"sleep-topdown-batch-11.png.bytes",2));
            result.Add(182049,new Entry(182,49,"sleep-topdown-batch-11.png.bytes",3));
            result.Add(182050,new Entry(182,50,"sleep-topdown-batch-11.png.bytes",4));
            result.Add(182051,new Entry(182,51,"sleep-topdown-batch-11.png.bytes",5));
            result.Add(182052,new Entry(182,52,"sleep-topdown-batch-11.png.bytes",6));
            result.Add(182053,new Entry(182,53,"sleep-topdown-batch-11.png.bytes",7));
            result.Add(182054,new Entry(182,54,"sleep-topdown-batch-12.png.bytes",0));
            result.Add(182055,new Entry(182,55,"sleep-topdown-batch-12.png.bytes",0));
            result.Add(182056,new Entry(182,56,"sleep-topdown-batch-12.png.bytes",1));
            result.Add(182057,new Entry(182,57,"sleep-topdown-batch-12.png.bytes",2));
            result.Add(357010,new Entry(357,10,"sleep-topdown-batch-12.png.bytes",2));
            result.Add(182058,new Entry(182,58,"sleep-topdown-batch-12.png.bytes",3));
            result.Add(182059,new Entry(182,59,"sleep-topdown-batch-12.png.bytes",4));
            result.Add(183000,new Entry(183,0,"sleep-topdown-batch-12.png.bytes",5));
            result.Add(183001,new Entry(183,1,"sleep-topdown-batch-12.png.bytes",6));
            result.Add(183002,new Entry(183,2,"sleep-topdown-batch-12.png.bytes",7));
            result.Add(334015,new Entry(334,15,"sleep-topdown-batch-12.png.bytes",7));
            result.Add(357012,new Entry(357,12,"sleep-topdown-batch-12.png.bytes",7));
            result.Add(183003,new Entry(183,3,"sleep-topdown-batch-13.png.bytes",0));
            result.Add(334016,new Entry(334,16,"sleep-topdown-batch-13.png.bytes",0));
            result.Add(346020,new Entry(346,20,"sleep-topdown-batch-13.png.bytes",0));
            result.Add(357013,new Entry(357,13,"sleep-topdown-batch-13.png.bytes",0));
            result.Add(183004,new Entry(183,4,"sleep-topdown-batch-13.png.bytes",1));
            result.Add(183005,new Entry(183,5,"sleep-topdown-batch-13.png.bytes",2));
            result.Add(183006,new Entry(183,6,"sleep-topdown-batch-13.png.bytes",3));
            result.Add(346021,new Entry(346,21,"sleep-topdown-batch-13.png.bytes",3));
            result.Add(183007,new Entry(183,7,"sleep-topdown-batch-13.png.bytes",4));
            result.Add(183008,new Entry(183,8,"sleep-topdown-batch-13.png.bytes",5));
            result.Add(357015,new Entry(357,15,"sleep-topdown-batch-13.png.bytes",5));
            result.Add(183009,new Entry(183,9,"sleep-topdown-batch-13.png.bytes",6));
            result.Add(183010,new Entry(183,10,"sleep-topdown-batch-13.png.bytes",7));
            result.Add(183011,new Entry(183,11,"sleep-topdown-batch-14.png.bytes",0));
            result.Add(183012,new Entry(183,12,"sleep-topdown-batch-14.png.bytes",1));
            result.Add(183013,new Entry(183,13,"sleep-topdown-batch-14.png.bytes",2));
            result.Add(334019,new Entry(334,19,"sleep-topdown-batch-14.png.bytes",2));
            result.Add(183014,new Entry(183,14,"sleep-topdown-batch-14.png.bytes",3));
            result.Add(183015,new Entry(183,15,"sleep-topdown-batch-14.png.bytes",4));
            result.Add(183016,new Entry(183,16,"sleep-topdown-batch-14.png.bytes",5));
            result.Add(334020,new Entry(334,20,"sleep-topdown-batch-14.png.bytes",5));
            result.Add(183017,new Entry(183,17,"sleep-topdown-batch-14.png.bytes",6));
            result.Add(183018,new Entry(183,18,"sleep-topdown-batch-14.png.bytes",7));
            result.Add(334021,new Entry(334,21,"sleep-topdown-batch-14.png.bytes",7));
            result.Add(183019,new Entry(183,19,"sleep-topdown-batch-15.png.bytes",0));
            result.Add(183020,new Entry(183,20,"sleep-topdown-batch-15.png.bytes",1));
            result.Add(183021,new Entry(183,21,"sleep-topdown-batch-15.png.bytes",2));
            result.Add(184000,new Entry(184,0,"sleep-topdown-batch-15.png.bytes",3));
            result.Add(184001,new Entry(184,1,"sleep-topdown-batch-15.png.bytes",4));
            result.Add(184002,new Entry(184,2,"sleep-topdown-batch-15.png.bytes",5));
            result.Add(184003,new Entry(184,3,"sleep-topdown-batch-15.png.bytes",6));
            result.Add(184004,new Entry(184,4,"sleep-topdown-batch-15.png.bytes",7));
            result.Add(184013,new Entry(184,13,"sleep-topdown-batch-17.png.bytes",0));
            result.Add(184014,new Entry(184,14,"sleep-topdown-batch-17.png.bytes",1));
            result.Add(184015,new Entry(184,15,"sleep-topdown-batch-17.png.bytes",2));
            result.Add(184016,new Entry(184,16,"sleep-topdown-batch-17.png.bytes",3));
            result.Add(184017,new Entry(184,17,"sleep-topdown-batch-17.png.bytes",4));
            result.Add(184018,new Entry(184,18,"sleep-topdown-batch-17.png.bytes",5));
            result.Add(184019,new Entry(184,19,"sleep-topdown-batch-17.png.bytes",6));
            result.Add(184020,new Entry(184,20,"sleep-topdown-batch-17.png.bytes",7));
            result.Add(184021,new Entry(184,21,"sleep-topdown-batch-18.png.bytes",0));
            result.Add(184022,new Entry(184,22,"sleep-topdown-batch-18.png.bytes",1));
            result.Add(184023,new Entry(184,23,"sleep-topdown-batch-18.png.bytes",2));
            result.Add(184024,new Entry(184,24,"sleep-topdown-batch-18.png.bytes",3));
            result.Add(184025,new Entry(184,25,"sleep-topdown-batch-18.png.bytes",4));
            result.Add(184026,new Entry(184,26,"sleep-topdown-batch-18.png.bytes",5));
            result.Add(184027,new Entry(184,27,"sleep-topdown-batch-18.png.bytes",6));
            result.Add(184028,new Entry(184,28,"sleep-topdown-batch-18.png.bytes",7));
            result.Add(184029,new Entry(184,29,"sleep-topdown-batch-19.png.bytes",0));
            result.Add(184030,new Entry(184,30,"sleep-topdown-batch-19.png.bytes",1));
            result.Add(184031,new Entry(184,31,"sleep-topdown-batch-19.png.bytes",2));
            result.Add(184032,new Entry(184,32,"sleep-topdown-batch-19.png.bytes",3));
            result.Add(184033,new Entry(184,33,"sleep-topdown-batch-19.png.bytes",4));
            result.Add(184034,new Entry(184,34,"sleep-topdown-batch-19.png.bytes",5));
            result.Add(334000,new Entry(334,0,"sleep-topdown-batch-19.png.bytes",6));
            result.Add(334001,new Entry(334,1,"sleep-topdown-batch-19.png.bytes",7));
            result.Add(334005,new Entry(334,5,"sleep-topdown-batch-20.png.bytes",0));
            result.Add(334011,new Entry(334,11,"sleep-topdown-batch-20.png.bytes",1));
            result.Add(334014,new Entry(334,14,"sleep-topdown-batch-20.png.bytes",2));
            result.Add(334017,new Entry(334,17,"sleep-topdown-batch-20.png.bytes",3));
            result.Add(334018,new Entry(334,18,"sleep-topdown-batch-20.png.bytes",4));
            result.Add(346000,new Entry(346,0,"sleep-topdown-batch-20.png.bytes",5));
            result.Add(346007,new Entry(346,7,"sleep-topdown-batch-20.png.bytes",6));
            result.Add(346011,new Entry(346,11,"sleep-topdown-batch-20.png.bytes",7));
            result.Add(346016,new Entry(346,16,"sleep-topdown-batch-21.png.bytes",0));
            result.Add(346017,new Entry(346,17,"sleep-topdown-batch-21.png.bytes",1));
            result.Add(346019,new Entry(346,19,"sleep-topdown-batch-21.png.bytes",2));
            result.Add(346022,new Entry(346,22,"sleep-topdown-batch-21.png.bytes",3));
            result.Add(357001,new Entry(357,1,"sleep-topdown-batch-21.png.bytes",4));
            result.Add(357007,new Entry(357,7,"sleep-topdown-batch-21.png.bytes",5));
            result.Add(381005,new Entry(381,5,"sleep-topdown-batch-21.png.bytes",6));
            result.Add(382005,new Entry(382,5,"sleep-topdown-batch-21.png.bytes",7));
            result.Add(383005,new Entry(383,5,"sleep-topdown-batch-22.png.bytes",0));
            result.Add(384005,new Entry(384,5,"sleep-topdown-batch-22.png.bytes",1));
            result.Add(395005,new Entry(395,5,"sleep-topdown-batch-22.png.bytes",2));
            result.Add(396005,new Entry(396,5,"sleep-topdown-batch-22.png.bytes",3));
            result.Add(397005,new Entry(397,5,"sleep-topdown-batch-22.png.bytes",4));
            result.Add(398005,new Entry(398,5,"sleep-topdown-batch-22.png.bytes",5));
            result.Add(387005,new Entry(387,5,"sleep-topdown-batch-22.png.bytes",6));
            result.Add(388005,new Entry(388,5,"sleep-topdown-batch-22.png.bytes",7));
            result.Add(389005,new Entry(389,5,"sleep-topdown-batch-23.png.bytes",0));
            result.Add(390005,new Entry(390,5,"sleep-topdown-batch-23.png.bytes",1));
            result.Add(392005,new Entry(392,5,"sleep-topdown-batch-23.png.bytes",2));
            result.Add(393005,new Entry(393,5,"sleep-topdown-batch-23.png.bytes",3));
            result.Add(451005,new Entry(451,5,"sleep-topdown-batch-23.png.bytes",4));
            result.Add(452005,new Entry(452,5,"sleep-topdown-batch-23.png.bytes",5));
            result.Add(385005,new Entry(385,5,"sleep-topdown-batch-23.png.bytes",6));
            result.Add(386005,new Entry(386,5,"sleep-topdown-batch-23.png.bytes",7));
            result.Add(391005,new Entry(391,5,"sleep-topdown-batch-24.png.bytes",0));
            result.Add(394005,new Entry(394,5,"sleep-topdown-batch-24.png.bytes",1));
            result.Add(453005,new Entry(453,5,"sleep-topdown-batch-24.png.bytes",2));
            result.Add(454005,new Entry(454,5,"sleep-topdown-batch-24.png.bytes",3));
            result.Add(455005,new Entry(455,5,"sleep-topdown-batch-24.png.bytes",4));
            result.Add(456005,new Entry(456,5,"sleep-topdown-batch-24.png.bytes",5));
            result.Add(399015,new Entry(399,15,"sleep-topdown-batch-24.png.bytes",6));
            return result;
        }
    }
}
