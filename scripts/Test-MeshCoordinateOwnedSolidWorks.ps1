# Uses a copied sample in a proven new, empty SW process. Never attaches to the
# user's running SW, saves source CAD, kills processes, or releases COM objects.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BuildDll,
    [Parameter(Mandatory=$true)][string]$InteropDirectory,
    [Parameter(Mandatory=$true)][string]$SeedPart,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell.exe -STA.' }
$BuildDll = (Resolve-Path -LiteralPath $BuildDll).Path
$SeedPart = (Resolve-Path -LiteralPath $SeedPart).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory must not already exist.' }
$interop = Join-Path $InteropDirectory 'SolidWorks.Interop.sldworks.dll'
$constants = Join-Path $InteropDirectory 'SolidWorks.Interop.swconst.dll'
[Reflection.Assembly]::LoadFrom($interop) | Out-Null
[Reflection.Assembly]::LoadFrom($constants) | Out-Null
Add-Type -ReferencedAssemblies @($interop, $constants, 'System.Core.dll') -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public static class OwnedMeshCoordinates {
 static void Require(bool value, string message) { if(!value) throw new InvalidOperationException(message); }
 static void Log(string text) { Console.WriteLine(DateTime.UtcNow.ToString("O")+" "+text); Console.Out.Flush(); }
 static object Helper(Type type, SldWorks app, ModelDoc2 doc) {
  object helper=FormatterServices.GetUninitializedObject(type);
  type.GetField("iSwApp").SetValue(helper,app);
  type.GetField("ActiveSWModel").SetValue(helper,doc);
  return helper;
 }
 static void Call(object helper,string method,params object[] args) {
  helper.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(helper,args);
 }
 static void WithRestore(object helper,Action operation) {
  var errors=new List<Exception>();
  try { operation(); } catch(Exception error) { errors.Add(error); }
  try { Call(helper,"ResetUserPreferences"); } catch(Exception error) { errors.Add(error); }
  if(errors.Count>0) throw new AggregateException("Probe operation/restoration failed",errors);
 }
 static double[][] Vertices(string path) {
  using(var file=File.OpenRead(path)) using(var reader=new BinaryReader(file)) {
   Require(file.Length>=84,"STL header missing"); reader.ReadBytes(80); uint count=reader.ReadUInt32();
   Require(count>0 && file.Length==84L+50L*count,"Not a valid binary STL");
   var points=new List<double[]>();
   for(uint f=0;f<count;f++) { reader.ReadBytes(12);
    for(int v=0;v<3;v++) points.Add(new double[]{reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()});
    reader.ReadUInt16();
   }
   return points.OrderBy(p=>p[0]).ThenBy(p=>p[1]).ThenBy(p=>p[2]).ToArray();
  }
 }
 static void Save(ModelDoc2 doc,string path) {
  doc.ClearSelection2(true); int errors=0,warnings=0;
  Require(doc.Extension.SaveAs(path,0,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null,ref errors,ref warnings),"SaveAs rejected");
  Require(errors==0 && File.Exists(path),"SaveAs errors="+errors);
  Log("SAVED "+Path.GetFileName(path)+" warnings="+warnings);
 }
 public static void Run(string dll,string seed,string root) {
  var previous=new HashSet<int>(Process.GetProcessesByName("SLDWORKS").Select(p=>p.Id));
  SldWorks app=null; ModelDoc2 doc=null; object original=null; bool originalSaved=false, owned=false; int pid=0; DateTime started=DateTime.MinValue;
  var failures=new List<Exception>();
  var type=Assembly.LoadFrom(dll).GetType("SW2URDF.URDFExport.ExportHelper",true);
  Log("BUILD mvid="+type.Assembly.ManifestModule.ModuleVersionId);
  try {
   app=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application",true));
   pid=app.GetProcessID(); Require(pid>0 && !previous.Contains(pid),"Existing SW returned; abort without touching documents");
   started=Process.GetProcessById(pid).StartTime;
   var docs=app.GetDocuments() as Array; Require(docs==null || docs.Length==0,"SW is not empty; abort");
   owned=true; Log("OWNED PID="+pid+" revision="+app.RevisionNumber());
   Directory.CreateDirectory(root); string copy=Path.Combine(root,"coordinate-fixture.SLDPRT"); File.Copy(seed,copy,false);
   int errors=0,warnings=0;
   doc=(ModelDoc2)app.OpenDoc6(copy,(int)swDocumentTypes_e.swDocPART,(int)swOpenDocOptions_e.swOpenDocOptions_Silent,"",ref errors,ref warnings);
   Require(doc!=null && errors==0,"Cannot open fixture copy");
   original=Helper(type,app,doc); Call(original,"SaveUserPreferences"); originalSaved=true;
   app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat,true);
   app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive,true);
   app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave,false);
   app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview,false);
   app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits,(int)swLengthUnit_e.swMETER);
   int legacy=(int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
   int modern=(int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
   Require(doc.Extension.SetUserPreferenceString(legacy,0,""),"Cannot set baseline document frame");
   Save(doc,Path.Combine(root,"baseline.stl"));
   doc.ClearSelection2(true); doc.SketchManager.Insert3DSketch(true);
   var point=doc.SketchManager.CreatePoint(0.017,-0.023,0.031);
   Require(point!=null,"Cannot create offset point"); doc.SketchManager.Insert3DSketch(true);
   doc.ClearSelection2(true); var selection=((SelectionMgr)doc.SelectionManager).CreateSelectData(); selection.Mark=1;
   Require(point.Select4(false,selection),"Cannot select coordinate origin");
   var coordinate=doc.FeatureManager.InsertCoordinateSystem(false,false,false);
   Require(coordinate!=null,"Cannot create coordinate frame"); coordinate.Name="ProbeOffset"; doc.ClearSelection2(true);
   Require(doc.Extension.SetUserPreferenceString(legacy,0,"ProbeOffset"),"Cannot set shifted frame");
   Require(doc.GetCurrentCoordinateSystemName()=="ProbeOffset","Shifted frame not effective");
   Save(doc,Path.Combine(root,"shifted.stl"));
   // Save a disposable CAD copy so the persisted/global frame is non-empty too.
   int saveErrors=0,saveWarnings=0;
   Require(doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent,ref saveErrors,ref saveWarnings) && saveErrors==0,"Fixture save failed");
   string global=app.GetUserPreferenceStringValue(modern), document=doc.Extension.GetUserPreferenceString(legacy,0);
   Log("BEFORE reset global=["+global+"] document=["+document+"] current=["+doc.GetCurrentCoordinateSystemName()+"]");
   var helper=Helper(type,app,doc); Call(helper,"SaveUserPreferences");
   WithRestore(helper,()=> {
    Call(helper,"ResetMeshExportCoordinateSystem",doc);
    Log("AFTER reset global=["+app.GetUserPreferenceStringValue(modern)+"] document=["+doc.Extension.GetUserPreferenceString(legacy,0)+"] current=["+doc.GetCurrentCoordinateSystemName()+"]");
    Save(doc,Path.Combine(root,"reset.stl"));
   });
   Require(doc.Extension.GetUserPreferenceString(legacy,0)==document && doc.GetCurrentCoordinateSystemName()==document && app.GetUserPreferenceStringValue(modern)==global,"Settings not restored");
   var a=Vertices(Path.Combine(root,"baseline.stl")); var b=Vertices(Path.Combine(root,"shifted.stl")); var c=Vertices(Path.Combine(root,"reset.stl"));
   Require(a.Length==b.Length && a.Length==c.Length,"Triangle counts differ");
   double max=0,shift=0;
   for(int i=0;i<a.Length;i++) for(int axis=0;axis<3;axis++) { max=Math.Max(max,Math.Abs(a[i][axis]-c[i][axis])); shift=Math.Max(shift,Math.Abs(a[i][axis]-b[i][axis])); }
   Require(max<1e-6 && shift>0.01,"Native coordinate check failed: reset="+max+" shift="+shift);
   Log("PASS vertices="+a.Length+" max reset error="+max+" shifted difference="+shift+"; settings restored");
   string localized=Path.Combine(root,"link-local.stl"); File.Copy(Path.Combine(root,"reset.stl"),localized,false);
   var frame=doc.Extension.GetCoordinateSystemTransformByName("ProbeOffset");
   var transform=type.GetMethod("TransformBinaryStlToFrame",BindingFlags.Static|BindingFlags.NonPublic);
   Require((bool)transform.Invoke(null,new object[]{localized,frame}),"Link transform failed");
   var local=Vertices(localized); Require(local.Length==b.Length,"Localized triangle count changed"); double localError=0;
   for(int i=0;i<b.Length;i++) for(int axis=0;axis<3;axis++) localError=Math.Max(localError,Math.Abs(local[i][axis]-b[i][axis]));
   Require(localError<1e-6,"Link-local STL differs from native named-frame export: "+localError);
   Log("PASS native named-frame versus plugin Link-local vertices; max error="+localError);
   try {
    WithRestore(helper,()=> { Call(helper,"ResetMeshExportCoordinateSystem",doc); throw new IOException("injected export failure"); });
   } catch(AggregateException error) { Require(error.InnerExceptions.Count==1 && error.InnerExceptions[0].Message=="injected export failure","Unexpected operation/restoration failure: "+error); }
   Require(doc.Extension.GetUserPreferenceString(legacy,0)==document && doc.GetCurrentCoordinateSystemName()==document && app.GetUserPreferenceStringValue(modern)==global,"Failure path settings not restored");
   Log("PASS coordinate restoration after injected export failure");
  } catch(Exception error) { failures.Add(error); }
  finally {
   if(owned) {
    bool canClean=false;
    try {
     Require(Process.GetProcessById(pid).StartTime==started && app.GetProcessID()==pid,"Process changed; refuse cleanup");
     var docs=app.GetDocuments() as Array;
     Require(docs==null || docs.Cast<object>().All(d=>Object.Equals(d,doc)),"Unowned document appeared; refuse cleanup");
     canClean=true;
    } catch(Exception error) { failures.Add(error); }
    if(canClean) {
     try { if(originalSaved) Call(original,"ResetUserPreferences"); } catch(Exception error) { failures.Add(error); }
     try { if(doc!=null) app.CloseDoc(doc.GetTitle()); } catch(Exception error) { failures.Add(error); }
     try {
      var remaining=app.GetDocuments() as Array; Require(remaining==null || remaining.Length==0,"Documents remain; refuse exit");
      app.ExitApp(); Log("Owned SW exited; source CAD untouched");
     } catch(Exception error) { failures.Add(error); }
    }
   }
  }
  if(failures.Count>0) throw new AggregateException("Native coordinate probe failed",failures);
 }
}
'@
[OwnedMeshCoordinates]::Run($BuildDll,$SeedPart,[IO.Path]::GetFullPath($OutputDirectory))
