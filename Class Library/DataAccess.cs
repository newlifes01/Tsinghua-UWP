﻿using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Web.Http;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Diagnostics;
using Windows.Storage;

namespace TsinghuaUWP
{
    // Data Access Object
    // retrieve from memory/ local settings/ remote

    static public class DataAccess
    {
        static ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
        static List<Course> courses = null;
        static List<Deadline> deadlines = null;
        static Semesters semesters = null;

        static public Windows.Foundation.Collections.IPropertySet getLocalSettings()
        {
            return localSettings.Values;
        }
        static public bool credentialAbsent()
        {
            var username = localSettings.Values["username"];
            return username == null
                || username.ToString() == "__anonymous";
        }
        static public bool supposedToWorkAnonymously()
        {
            var username = localSettings.Values["username"];
            return username != null
                && username.ToString() == "__anonymous";
        }
        static public bool isDemo()
        {
            return 
                localSettings.Values["username"] != null &&
                localSettings.Values["username"].ToString() == "_demo";
        }
        static public void setLocalSettings<Type>(string key, Type value)
        {
            localSettings.Values[key] = value;
        }
        static async public Task<int> updateAllFromRemote()
        {
            await getSemester(true);
            await getCourses(true);
            await getTimetable(true);
            await getAllDeadlines(true);
            return 0;
        }
        static public async Task<List<Deadline>> getDeadlinesFiltered(bool forceRemote = false, int limit = -1)
        {
            var assignments = await getAllDeadlines(forceRemote);

            var future = (from assignment in assignments
                          where assignment.hasBeenFinished == false && assignment.isPast() == false
                          orderby assignment.daysFromNow() ascending
                          select assignment);

            int futureCount = future.Count();

            if (futureCount > limit && limit >= 0)
            {
                return future.Take(limit).ToList();
            }


            var past = (from assignment in assignments
                        where assignment.hasBeenFinished == false && assignment.isPast() == true
                        orderby assignment.daysFromNow() descending
                        select assignment);


            if (limit < 0)
            {
                return future.Concat(past).ToList();
            }

            return future.Concat(past.Take(limit - futureCount)).ToList();
        }
        public static async Task<List<Course>> getCourses(bool forceRemote = false)
        {
            if (!forceRemote)
            {
                //try memory
                if (courses != null)
                {
                    Debug.WriteLine("[getCourses] Returning memory");
                    return courses;
                }

                //try localSettings
                var localCourses = localSettings.Values["courses"];
                if (localCourses != null)
                {
                    Debug.WriteLine("[getCourses] Returning local settings");
                    courses = JSON.parse<List<Course>>((string)localCourses);
                    return courses;
                }
            }


            //fetch from remote
            var _courses = await Remote.getRemoteCourseList();
            courses = _courses;
            localSettings.Values["courses"] = JSON.stringify(_courses);
            Debug.WriteLine("[getCourses] Returning remote");
            return courses;
        }
        public static async Task<Timetable> getTimetable(bool forceRemote = false)
        {
            //fetch from remote
            var _remoteTimetable = await Remote.getRemoteTimetable();
            Debug.WriteLine("[getTimetable] Returning remote");
            return _remoteTimetable;
        }
        public static async Task<Semester> getSemester(bool forceRemote = false)
        {
            if (forceRemote == false)
            {
                Semesters __semesters = null;
                //try memory
                if (semesters != null)
                {
                    Debug.WriteLine("[getCalendar] memory");
                    __semesters = semesters;
                }
                else //try localSettings
                {
                    var localJSON = localSettings.Values["semesters"];
                    if (localJSON != null)
                    {
                        Debug.WriteLine("[getCalendar] local settings");
                        __semesters = JSON.parse<Semesters>((string)localJSON);
                    }
                }

                //cache hit
                if (__semesters != null)
                {
                    if (DateTime.Parse(__semesters.currentSemester.endDate + " 23:59") < DateTime.Now)
                    {
                        //perform a remote update
                        Task task = getSemester(true);
                        task.ContinueWith((_) =>
                        {
                            Appointment.updateCalendar();
                        });

                        Debug.WriteLine("[getCalendar] Returning cache next");
                        return __semesters.nextSemester;
                    }
                    Debug.WriteLine("[getCalendar] Returning cache");
                    return __semesters.currentSemester;
                }
            }

            //fetch from remote
            Semesters _remoteSemesters = null;

            try {
                _remoteSemesters = await Remote.getHostedSemesters();
            } catch (Exception) { }

            if (_remoteSemesters == null)
            {
                Debug.WriteLine("[getCalendar] hosted fail, falling back");

                if (DataAccess.credentialAbsent() == true)
                    throw new LoginException("calendar_fall_back");

                _remoteSemesters = await Remote.getRemoteSemesters();
            }

            semesters = _remoteSemesters;
            localSettings.Values["semesters"] = JSON.stringify(semesters);
            Debug.WriteLine("[getCalendar] Returning remote");
            return semesters.currentSemester;
        }
        static public async Task<List<Deadline>> getAllDeadlines(bool forceRemote = false)
        {

            if (!forceRemote)
            {
                //try session memory
                if (deadlines != null)
                {
                    Debug.WriteLine("[getAllDeadlines] Returning memory");
                    return deadlines;
                }


                //try localSettings
                var local = localSettings.Values["deadlines"];
                if (local != null)
                {
                    Debug.WriteLine("[getAllDeadlines] Returning local settings");
                    return JSON.parse<List<Deadline>>((string)local);
                }
            }

            //fetch from remote

            List<Deadline> _deadlines = new List<Deadline>();

            foreach (var course in await getCourses(forceRemote))
            {
                Debug.WriteLine("[getAllDeadlines] Remote " + course.name);
                var id = course.id;
                List<Deadline> __deadlines;
                if (course.isNew)
                    __deadlines = await Remote.getRemoteHomeworkListNew(id);
                else
                    __deadlines = await Remote.getRemoteHomeworkList(id);
                _deadlines = _deadlines.Concat(__deadlines).ToList();
            }


            deadlines = _deadlines;
            localSettings.Values["deadlines"] = JSON.stringify(_deadlines);
            Debug.WriteLine("[getAllDeadlines] Returning remote");

            return _deadlines;
        }

    }

}
