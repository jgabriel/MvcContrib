// Copyright 2004-2007 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// MODIFICATIONS HAVE BEEN MADE TO THIS FILE

namespace MvcContrib.BrailViewEngine
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Configuration;
	using System.IO;
	using System.Reflection;
	using System.Runtime.Serialization;
	using System.Text;
	using System.Threading;
	using System.Web;
	using System.Web.Mvc;
	using System.Runtime.CompilerServices;
	using Boo.Lang.Runtime;
	using ViewFactories;
	using Boo.Lang.Compiler;
	using Boo.Lang.Compiler.IO;
	using Boo.Lang.Compiler.Pipelines;
	using Boo.Lang.Compiler.Steps;
	using Boo.Lang.Parser;

	public class BooViewEngine
	{
		private static BooViewEngineOptions options;

		/// <summary>
		/// This field holds all the cache of all the 
		/// compiled types (not instances) of all the views that Brail nows of.
		/// </summary>
		private readonly Hashtable compilations = Hashtable.Synchronized(
			new Hashtable(StringComparer.InvariantCultureIgnoreCase));

		/// <summary>
		/// used to hold the constructors of types, so we can avoid using
		/// Activator (which takes a long time
		/// </summary>
		private readonly Hashtable constructors = new Hashtable();

		private string baseSavePath;

		/// <summary>
		/// This is used to add a reference to the common scripts for each compiled scripts
		/// </summary>
		private Assembly common;


		public virtual bool SupportsJSGeneration
		{
			get { return true; }
		}

		public virtual string ViewFileExtension
		{
			get { return ".brail"; }
		}

		public virtual string JSGeneratorFileExtension
		{
			get { return ".brailjs"; }
		}

		private IViewSourceLoader _viewSourceLoader;
		public IViewSourceLoader ViewSourceLoader
		{
			get
			{
				if(_viewSourceLoader == null)
				{
					SetViewSourceLoader(new FileSystemViewSourceLoader());
				}
				return _viewSourceLoader;
			}
			set
			{
				SetViewSourceLoader(value);
			}
		}

		public void SetViewSourceLoader(IViewSourceLoader viewSourceLoader)
		{
			if( _viewSourceLoader != null )
			{
				_viewSourceLoader.ViewRootDirectoryChanged -= OnViewChanged;
			}
			_viewSourceLoader = viewSourceLoader;
			if( _viewSourceLoader != null )
			{
				_viewSourceLoader.ViewRootDirectoryChanged += OnViewChanged;
			}
		}

		public string ViewRootDir
		{
			get { return ViewSourceLoader.ViewRootDirectory; }
		}

		public BooViewEngineOptions Options
		{
			get { return options; }
			set { options = value; }
		}

		#region IInitializable Members

		public void Initialize()
		{
			if (options == null) InitializeConfig();

			string baseDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
			Log("Base Directory: " + baseDir);
			baseSavePath = Path.Combine(baseDir, options.SaveDirectory);
			Log("Base Save Path: " + baseSavePath);

			if (options.SaveToDisk && !Directory.Exists(baseSavePath))
			{
				Directory.CreateDirectory(baseSavePath);
				Log("Created directory " + baseSavePath);
			}

			CompileCommonScripts();

			// Register extension methods
			foreach( Assembly assembly in options.AssembliesToReference )
			{
				if( assembly.GetCustomAttributes(typeof(ExtensionAttribute), true).Length > 0 )
				{
					foreach( var type in assembly.GetTypes() )
					{
						foreach(string nmespace in options.NamespacesToImport)
						{
							if( type != null && type.Namespace != null && type.Namespace.Equals(nmespace))
							{
								RuntimeServices.RegisterExtensions(type);
							}	
						}
					}
				}
			}

//			ViewSourceLoader.ViewChanged += OnViewChanged;
		}

		#endregion

		// Process a template name and output the results to the user
		// This may throw if an error occured and the user is not local (which would 
		// cause the yellow screen of death)
		public virtual BrailBase Process(string viewName, string masterName)
//		(String templateName, TextWriter output, IEngineContext context, IController controller, IControllerContext controllerContext)
		{
			Log("Starting to process request for {0}", viewName);
			string file = viewName + ViewFileExtension;
			// Will compile on first time, then save the assembly on the cache.
			BrailBase view = GetCompiledScriptInstance(file);
			view.Layout = GetOutput(masterName);

			return view;

//			controller.PreSendView(view);
//
//			Log("Executing view {0}", viewName);
//
//			try
//			{
//				view.Run();
//			}
//			catch(Exception e)
//			{
//				HandleException(viewName, view, e);
//			}
//
//			if (layoutViewOutput.Layout != null)
//			{
//				layoutViewOutput.Layout.SetParent(view);
//				try
//				{
//					layoutViewOutput.Layout.Run();
//				}
//				catch(Exception e)
//				{
//					HandleException(masterName, layoutViewOutput.Layout, e);
//				}
//			}
//			Log("Finished executing view {0}", viewName);
//			controller.PostSendView(view);
		}

//		public override void Process(string templateName, string layoutName, TextWriter output,
//		                             IDictionary<string, object> parameters)
//		{
//			throw new NotImplementedException();
//		}

		public virtual BrailBase ProcessPartial(string viewName)
//		(string partialName, TextWriter output, IEngineContext context, IController controller, IControllerContext controllerContext)
		{
			Log("Generating partial for {0}", viewName);

			try
			{
				string file = ResolveTemplateName(viewName, ViewFileExtension);
				BrailBase view = GetCompiledScriptInstance(file);
				return view;
			}
			catch(Exception ex)
			{
//				if (Logger != null && Logger.IsErrorEnabled)
//				{
//					Logger.Error("Could not generate JS", ex);
//				}

				throw new Exception("Error generating partial: " + viewName, ex);
			}
		}

//		public override object CreateJSGenerator(JSCodeGeneratorInfo generatorInfo, IEngineContext context,
//		                                         IController controller,
//		                                         IControllerContext controllerContext)
//		{
//			return new BrailJSGenerator(generatorInfo.CodeGenerator, generatorInfo.LibraryGenerator,
//			                            generatorInfo.Extensions, generatorInfo.ElementExtensions);
//		}
//
//		public override void GenerateJS(string templateName, TextWriter output, JSCodeGeneratorInfo generatorInfo,
//		                                IEngineContext context, IController controller, IControllerContext controllerContext)
//		{
//			Log("Generating JS for {0}", templateName);
//
//			try
//			{
//				object generator = CreateJSGenerator(generatorInfo, context, controller, controllerContext);
//				AdjustJavascriptContentType(context);
//				string file = ResolveJSTemplateName(templateName);
//				BrailBase view = GetCompiledScriptInstance(file,
//				                                           //we use the script just to build the generator, not to output to the user
//				                                           new StringWriter(),
//				                                           context, controller, controllerContext);
//				Log("Executing JS view {0}", templateName);
//				view.AddProperty("page", generator);
//				view.Run();
//
//				output.WriteLine(generator);
//				Log("Finished executing JS view {0}", templateName);
//			}
//			catch(Exception ex)
//			{
//				if (Logger != null && Logger.IsErrorEnabled)
//				{
//					Logger.Error("Could not generate JS", ex);
//				}
//
//				throw new MonoRailException("Error generating JS. Template: " + templateName, ex);
//			}
//		}
//
//		/// <summary>
//		/// Wraps the specified content in the layout using the
//		/// context to output the result.
//		/// </summary>
//		/// <param name="contents"></param>
//		/// <param name="context"></param>
//		/// <param name="controller"></param>
//		/// <param name="controllerContext"></param>
//		public virtual void RenderStaticWithinLayout(String contents, string masterName, ControllerContext controllerContext)
//		{
//			LayoutViewOutput layoutViewOutput = GetOutput(masterName);
//			layoutViewOutput.Output.Write(contents);
//			// here we don't need to pass parameters from the layout to the view, 
//			if (layoutViewOutput.Layout != null)
//			{
//				layoutViewOutput.Layout.Run();
//			}
//		}

		private void OnViewChanged(object sender, FileSystemEventArgs e)
		{
			string path = e.FullPath.Substring(ViewRootDir.Length);
			if (path.Length > 0 && (path[0] == Path.DirectorySeparatorChar ||
			                        path[0] == Path.AltDirectorySeparatorChar))
			{
				path = path.Substring(1);
			}
			if (path.IndexOf(options.CommonScriptsDirectory) != -1)
			{
				Log("Detected a change in commons scripts directory " + options.CommonScriptsDirectory + ", recompiling site");
				// need to invalidate the entire CommonScripts assembly
				// not worrying about concurrency here, since it is assumed
				// that changes here are rare. Note, this force a recompile of the 
				// whole site!
				try
				{
					WaitForFileToBecomeAvailableForReading(e);
					CompileCommonScripts();
				}
				catch(Exception ex)
				{
					// we failed to recompile the commons scripts directory, but because we are running
					// on another thread here, and exception would kill the application, so we log it 
					// and continue on. CompileCommonScripts() will only change the global state if it has
					// successfully compiled the commons scripts directory.
					Log("Failed to recompile the commons scripts directory! {0}", ex);
				}
			}
			else
			{
				Log("Detected a change in {0}, removing from complied cache", e.Name);
				// Will cause a recompilation
				compilations[path] = null;
			}
		}

		private static void WaitForFileToBecomeAvailableForReading(FileSystemEventArgs e)
		{
			// We may need to wait while the file is being written and closed to disk
			int retries = 10;
			bool successfullyOpenedFile = false;
			while(retries != 0 && successfullyOpenedFile == false)
			{
				retries -= 1;
				try
				{
					using(File.OpenRead(e.FullPath))
					{
						successfullyOpenedFile = true;
					}
				}
				catch(IOException)
				{
					//The file is probably in locked because it is currently being written to,
					// will wait a while for it to be freed.
					// again, this isn't something that need to be very robust, it runs on a separate thread
					// and if it fails, it is not going to do any damage
					Thread.Sleep(250);
				}
			}
		}

		// Get configuration options if they exists, if they do not exist, load the default ones
		// Create directory to save the compiled assemblies if required.
		// pre-compile the common scripts
//		public override void Service(IServiceProvider serviceProvider)
//		{
//			base.Service(serviceProvider);
//			ILoggerFactory loggerFactory = serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
//			if (loggerFactory != null)
//				logger = loggerFactory.Create(GetType());
//		}

		protected static string ResolveTemplateName(string templateName, string extention)
		{
			if (Path.HasExtension(templateName))
			{
				return templateName.ToUpper();
			}
			else
			{
				return templateName.ToUpper() + extention;
			}
		}

		// Check if a layout has been defined. If it was, then the layout would be created
		// and will take over the output, otherwise, the context.Reposne.Output is used, 
		// and layout is null
		private BrailBase GetOutput(string masterName)
		{
			BrailBase layout = null;
			if (!string.IsNullOrEmpty(masterName))
			{
				string layoutTemplate = masterName;
				if (layoutTemplate.StartsWith("/") == false)
				{
					layoutTemplate = "layouts\\" + layoutTemplate;
				}
				string layoutFilename = layoutTemplate + ViewFileExtension;
				layout = GetCompiledScriptInstance(layoutFilename);
			}
			return layout;
		}

		/// <summary>
		/// This takes a filename and return an instance of the view ready to be used.
		/// If the file does not exist, an exception is raised
		/// The cache is checked to see if the file has already been compiled, and it had been
		/// a check is made to see that the compiled instance is newer then the file's modification date.
		/// If the file has not been compiled, or the version on disk is newer than the one in memory, a new
		/// version is compiled.
		/// Finally, an instance is created and returned	
		/// </summary>
		public BrailBase GetCompiledScriptInstance(string file)
//		(string file, TextWriter output, IEngineContext context, IController controller, IControllerContext controllerContext)
		{
			bool batch = options.BatchCompile;

			// normalize filename - replace / or \ to the system path seperator
			string filename = file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
			if (filename[0] == Path.DirectorySeparatorChar)
				filename = filename.Substring(1);

			Log("Getting compiled instnace of {0}", filename);

			Type type;

			if (compilations.ContainsKey(filename))
			{
				type = (Type) compilations[filename];
				if (type != null)
				{
					Log("Got compiled instance of {0} from cache", filename);
					return CreateBrailBase(type);
				}
				// if file is in compilations and the type is null,
				// this means that we need to recompile. Since this usually means that 
				// the file was changed, we'll set batch to false and procceed to compile just
				// this file.
				Log("Cache miss! Need to recompile {0}", filename);
				batch = false;
			}

			type = CompileScript(filename, batch);

			if (type == null)
			{
				throw new Exception("Could not find a view with path " + filename);
			}

			return CreateBrailBase(type);
		}

		private BrailBase CreateBrailBase(Type type)
//		(IEngineContext context, IController controller, IControllerContext controllerContext,TextWriter output, Type type)
		{
			ConstructorInfo constructor = (ConstructorInfo) constructors[type];
			var self = (BrailBase) FormatterServices.GetUninitializedObject(type);
			constructor.Invoke(self, new object[] { this } ); //, context, controller, controllerContext});
			return self;
		}

		// Compile a script (or all scripts in a directory), save the compiled result
		// to the cache and return the compiled type.
		// If an error occurs in batch compilation, then an attempt is made to compile just the single
		// request file.
		public Type CompileScript(string filename, bool batch)
		{
			IDictionary<ICompilerInput, string> inputs2FileName = GetInput(filename, batch);
			string name = NormalizeName(filename);
			Log("Compiling {0} to {1} with batch: {2}", filename, name, batch);
			CompilationResult result = DoCompile(inputs2FileName.Keys, name);

			if (result.Context.Errors.Count > 0)
			{
				if (batch == false)
				{
					RaiseCompilationException(filename, inputs2FileName, result);
				}
				//error compiling a batch, let's try a single file
				return CompileScript(filename, false);
			}
			Type type;
			foreach(var input in inputs2FileName.Keys)
			{
				string viewName = Path.GetFileNameWithoutExtension(input.Name);
				string typeName = TransformToBrailStep.GetViewTypeName(viewName);
				type = result.Context.GeneratedAssembly.GetType(typeName);
				Log("Adding {0} to the cache", type.FullName);
				compilations[inputs2FileName[input]] = type;
				constructors[type] = type.GetConstructor(new[] { typeof(BooViewEngine) });
//																								  	 typeof(TextWriter)																				 
//				                                         		typeof(IEngineContext),
//				                                         		typeof(IController),
//				                                         		typeof(IControllerContext)
//				                                         	});
			}
			type = (Type) compilations[filename];
			return type;
		}

		private void RaiseCompilationException(string filename, IDictionary<ICompilerInput, string> inputs2FileName,
		                                       CompilationResult result)
		{
			string errors = result.Context.Errors.ToString(true);
			Log("Failed to compile {0} because {1}", filename, errors);
			var code = new StringBuilder();
			foreach(var input in inputs2FileName.Keys)
			{
				code.AppendLine()
					.Append(result.Processor.GetInputCode(input))
					.AppendLine();
			}
			throw new HttpParseException("Error compiling Brail code",
			                             result.Context.Errors[0],
			                             filename,
			                             code.ToString(), result.Context.Errors[0].LexicalInfo.Line);
		}

		// If batch compilation is set to true, this would return all the view scripts
		// in the director (not recursive!)
		// Otherwise, it would return just the single file
		private IDictionary<ICompilerInput, string> GetInput(string filename, bool batch)
		{
			var input2FileName = new Dictionary<ICompilerInput, string>();
			if (batch == false)
			{
				input2FileName.Add(CreateInput(filename), filename);
				return input2FileName;
			}
			// use the System.IO.Path to get the folder name even though
			// we are using the ViewSourceLoader to load the actual file
			string directory = Path.GetDirectoryName(filename);
			foreach(var file in ViewSourceLoader.ListViews(directory))
			{
				ICompilerInput input = CreateInput(file);
				input2FileName.Add(input, file);
			}
			return input2FileName;
		}

		// create an input from a resource name
		public ICompilerInput CreateInput(string name)
		{
			IViewSource viewSrc = ViewSourceLoader.GetViewSource(name);
			if (viewSrc == null)
			{
				throw new Exception(string.Format("{0} is not a valid view", name));
			}
			// I need to do it this way because I can't tell 
			// when to dispose of the stream. 
			// It is not expected that this will be a big problem, the string
			// will go away after the compile is done with them.
			using(var stream = new StreamReader(viewSrc.OpenViewStream()))
			{
				return new StringInput(name, stream.ReadToEnd());
			}
		}

		/// <summary>
		/// Perform the actual compilation of the scripts
		/// Things to note here:
		/// * The generated assembly reference the Castle.MonoRail.MonoRailBrail and Castle.MonoRail.Framework assemblies
		/// * If a common scripts assembly exist, it is also referenced
		/// * The AddBrailBaseClassStep compiler step is added - to create a class from the view's code
		/// * The ProcessMethodBodiesWithDuckTyping is replaced with ReplaceUknownWithParameters
		///   this allows to use naked parameters such as (output context.IsLocal) without using 
		///   any special syntax
		/// * The FixTryGetParameterConditionalChecks is run afterward, to transform "if ?Error" to "if not ?Error isa IgnoreNull"
		/// * The ExpandDuckTypedExpressions is replace with a derived step that allows the use of Dynamic Proxy assemblies
		/// * The IntroduceGlobalNamespaces step is removed, to allow to use common variables such as 
		///   date and list without accidently using the Boo.Lang.BuiltIn versions
		/// </summary>
		/// <param name="files"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		private CompilationResult DoCompile(IEnumerable<ICompilerInput> files, string name)
		{
			ICompilerInput[] filesAsArray = new List<ICompilerInput>(files).ToArray();
			BooCompiler compiler = SetupCompiler(filesAsArray);
			string filename = Path.Combine(baseSavePath, name);
			compiler.Parameters.OutputAssembly = filename;
			// this is here and not in SetupCompiler since CompileCommon is also
			// using SetupCompiler, and we don't want reference to the old common from the new one
			if (common != null)
				compiler.Parameters.References.Add(common);
			// pre procsssor needs to run before the parser
			var processor = new BrailPreProcessor(this);
			compiler.Parameters.Pipeline.Insert(0, processor);
			// inserting the add class step after the parser
			compiler.Parameters.Pipeline.Insert(2, new TransformToBrailStep(options));
			compiler.Parameters.Pipeline.Replace(typeof(ProcessMethodBodiesWithDuckTyping),
			                                     new ReplaceUknownWithParameters());
			//compiler.Parameters.Pipeline.Replace(typeof(ExpandDuckTypedExpressions),
			//                                     new ExpandDuckTypedExpressions_WorkaroundForDuplicateVirtualMethods());
			compiler.Parameters.Pipeline.Replace(typeof(InitializeTypeSystemServices),
			                                     new InitializeCustomTypeSystem());
			compiler.Parameters.Pipeline.InsertBefore(typeof(ReplaceUknownWithParameters),
			                                          new FixTryGetParameterConditionalChecks());
			compiler.Parameters.Pipeline.RemoveAt(compiler.Parameters.Pipeline.Find(typeof(IntroduceGlobalNamespaces)));

			return new CompilationResult(compiler.Run(), processor);
		}

		// Return the output filename for the generated assembly
		// The filename is dependant on whatever we are doing a batch
		// compile or not, if it's a batch compile, then the directory name
		// is used, if it's just a single file, we're using the file's name.
		// '/' and '\' are replaced with '_', I'm not handling ':' since the path
		// should never include it since I'm converting this to a relative path
		public string NormalizeName(string filename)
		{
			string name = filename;
			name = name.Replace(Path.AltDirectorySeparatorChar, '_');
			name = name.Replace(Path.DirectorySeparatorChar, '_');

			return name + "_BrailView.dll";
		}

		// Compile all the common scripts to a common assemblies
		// an error in the common scripts would raise an exception.
		public bool CompileCommonScripts()
		{
			if (options.CommonScriptsDirectory == null)
				return false;

			// the demi.boo is stripped, but GetInput require it.
			string demiFile = Path.Combine(options.CommonScriptsDirectory, "demi.brail");
			IDictionary<ICompilerInput, string> inputs = GetInput(demiFile, true);
			ICompilerInput[] inputsAsArray = new List<ICompilerInput>(inputs.Keys).ToArray();
			BooCompiler compiler = SetupCompiler(inputsAsArray);
			string outputFile = Path.Combine(baseSavePath, "CommonScripts.dll");
			compiler.Parameters.OutputAssembly = outputFile;
			CompilerContext result = compiler.Run();
			if (result.Errors.Count > 0)
				throw new Exception(result.Errors.ToString(true));
			common = result.GeneratedAssembly;
			compilations.Clear();
			return true;
		}

		// common setup for the compiler
		private static BooCompiler SetupCompiler(IEnumerable<ICompilerInput> files)
		{
			var compiler = new BooCompiler();
			compiler.Parameters.Ducky = true;
			compiler.Parameters.Debug = options.Debug;
			if (options.SaveToDisk)
				compiler.Parameters.Pipeline = new CompileToFile();
			else
				compiler.Parameters.Pipeline = new CompileToMemory();
			// replace the normal parser with white space agnostic one.
			compiler.Parameters.Pipeline.RemoveAt(0);
			compiler.Parameters.Pipeline.Insert(0, new WSABooParsingStep());
			foreach(var file in files)
			{
				compiler.Parameters.Input.Add(file);
			}
			foreach(Assembly assembly in options.AssembliesToReference)
			{
				compiler.Parameters.References.Add(assembly);
			}
			compiler.Parameters.OutputType = CompilerOutputType.Library;
			return compiler;
		}

		private static void InitializeConfig()
		{
			InitializeConfig("brail");

			if (options == null)
			{
				InitializeConfig("Brail");
			}

			if (options == null)
			{
				options = new BooViewEngineOptions();
			}
		}

		private static void InitializeConfig(string sectionName)
		{
			options = ConfigurationManager.GetSection(sectionName) as BooViewEngineOptions;
		}

		private void Log(string msg, params object[] items)
		{
//			if (logger == null || logger.IsDebugEnabled == false)
//				return;
//			logger.DebugFormat(msg, items);
		}

		public bool ConditionalPreProcessingOnly(string name)
		{
			return String.Equals(
				Path.GetExtension(name),
				JSGeneratorFileExtension,
				StringComparison.InvariantCultureIgnoreCase);
		}

		#region Nested type: CompilationResult

		private class CompilationResult
		{
			private readonly CompilerContext context;
			private readonly BrailPreProcessor processor;

			public CompilationResult(CompilerContext context, BrailPreProcessor processor)
			{
				this.context = context;
				this.processor = processor;
			}

			public CompilerContext Context
			{
				get { return context; }
			}

			public BrailPreProcessor Processor
			{
				get { return processor; }
			}
		}

		#endregion

		#region Nested type: LayoutViewOutput

		private class LayoutViewOutput
		{
			private readonly BrailBase layout;
			private readonly TextWriter output;

			public LayoutViewOutput(TextWriter output, BrailBase layout)
			{
				this.layout = layout;
				this.output = output;
			}

			public BrailBase Layout
			{
				get { return layout; }
			}

			public TextWriter Output
			{
				get { return output; }
			}
		}

		#endregion
	}
}
