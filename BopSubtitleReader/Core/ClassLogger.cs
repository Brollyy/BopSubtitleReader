using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BopSubtitleReader.Core;

public sealed class ClassLogger
{
	public static ClassLogger? Instance { get; private set; }

	private readonly ConfigEntry<LogLevel>? _minimumLevel;
	private readonly ManualLogSource? _logSource;
	private readonly Dictionary<string, ClassLogger>? _cache;
	private readonly string? _className;

	public static void Initialize(ConfigFile config, string pluginGuid)
	{
		Instance = new ClassLogger(config, pluginGuid);
	}

	public static ClassLogger GetForClass<T>()
	{
		return GetForClass(typeof(T));
	}

	public static ClassLogger GetForClass(Type type)
	{
		return Instance is null
			? throw new InvalidOperationException("ClassLogger is not initialized. Call ClassLogger.Initialize(...) first.")
			: Instance.For(type);
	}

	private ClassLogger(ConfigFile config, string pluginGuid)
	{
		_minimumLevel = config.Bind(
			"Logging",
			"MinimumLevel",
			LogLevel.Info,
			$"Minimum log level for {pluginGuid} diagnostic output.");
		_logSource = Logger.CreateLogSource(pluginGuid);
		_cache = new Dictionary<string, ClassLogger>(StringComparer.Ordinal);
	}

	private ClassLogger(ManualLogSource logSource, ConfigEntry<LogLevel> minimumLevel, string className)
	{
		_logSource = logSource;
		_minimumLevel = minimumLevel;
		_className = className;
	}

	public ClassLogger For<T>()
	{
		return For(typeof(T));
	}

	public ClassLogger For(Type type)
	{
		if (_cache is null || _logSource is null || _minimumLevel is null)
		{
			throw new InvalidOperationException("Use the singleton manager instance to resolve class loggers.");
		}

		string className = type.Name;
		if (_cache.TryGetValue(className, out ClassLogger? existing))
		{
			return existing;
		}

		var logger = new ClassLogger(_logSource, _minimumLevel, className);
		_cache[className] = logger;
		return logger;
	}

	public void Trace(string message) => Log(LogLevel.Debug, message);
	public void Info(string message) => Log(LogLevel.Info, message);
	public void Warn(string message) => Log(LogLevel.Warning, message);
	public void Error(string message) => Log(LogLevel.Error, message);

	private void Log(LogLevel level, string message)
	{
		if (_logSource is null || _minimumLevel is null || _className is null)
		{
			throw new InvalidOperationException("Use ClassLogger.GetForClass<T>() to obtain a class logger instance.");
		}

		if (level < _minimumLevel.Value)
		{
			return;
		}

		string formatted = $"[{_className}] {message}";
		switch (level)
		{
			case LogLevel.Debug:
				_logSource.LogDebug(formatted);
				break;
			case LogLevel.Info:
				_logSource.LogInfo(formatted);
				break;
			case LogLevel.Warning:
				_logSource.LogWarning(formatted);
				break;
			case LogLevel.Error:
				_logSource.LogError(formatted);
				break;
			case LogLevel.Fatal:
				_logSource.LogFatal(formatted);
				break;
			default:
				_logSource.Log(level, formatted);
				break;
		}
	}
}
