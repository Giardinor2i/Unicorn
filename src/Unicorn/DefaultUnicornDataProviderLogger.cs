﻿using System;
using Rainbow.Model;
using Sitecore.Data.Items;
using Unicorn.Data;
using Unicorn.Logging;

namespace Unicorn
{
	/// <summary>
	/// A logger implementation for the UnicornDataProvider that sends log entries to the configured ILogger
	/// </summary>
	public class DefaultUnicornDataProviderLogger : IUnicornDataProviderLogger
	{
		private readonly ILogger _logger;

		public DefaultUnicornDataProviderLogger(ILogger logger)
		{
			_logger = logger;
		}

		public virtual void RenamedItem(string providerName, ISerializableItem sourceItem, string oldName)
		{
			_logger.Info(string.Format("{0}: Renamed serialized item to {1} from {2}", providerName, sourceItem.Path, oldName));
		}

		public virtual void SavedItem(string providerName, ISerializableItem sourceItem, string triggerReason)
		{
			_logger.Info(string.Format("{0}: Serialized {1} ({2}) to disk ({3}).", providerName, sourceItem.Path, sourceItem.Id, triggerReason));
		}

		public virtual void MovedItemToNonIncludedLocation(string providerName, ISerializableItem existingItem)
		{
			_logger.Debug(string.Format("{0}: Moved item {1} was moved to a path that was not included in serialization, and the existing serialized item was deleted.", providerName, existingItem.Path));
		}

		public virtual void MovedItem(string providerName, ISerializableItem sourceItem, ISerializableItem destinationItem)
		{
			_logger.Info(string.Format("{0}: Moved serialized item {1} ({2}) to {3}", providerName, sourceItem.Path, sourceItem.Id, destinationItem.Path));
		}

		public virtual void CopiedItem(string providerName, Func<ISerializableItem> sourceItem, ISerializableItem copiedItem)
		{
		}

		public virtual void DeletedItem(string providerName, ISerializableItem existingItem)
		{
			_logger.Info(string.Format("{0}: Serialized item {1} was deleted due to the source item being deleted.", providerName, existingItem.GetDisplayIdentifier()));
		}

		public virtual void SaveRejectedAsInconsequential(string providerName, ItemChanges changes)
		{
			_logger.Debug(string.Format("{0}: Ignored save of {1} because it contained no consequential item changes.", providerName, changes.Item.Paths.Path));
		}
	}
}
