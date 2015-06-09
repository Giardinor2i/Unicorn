﻿using System;
using Rainbow.Model;

namespace Unicorn.Evaluators
{
	public interface ISerializedAsMasterEvaluatorLogger
	{
		/// <summary>
		/// Called when an item is evaluated for deletion
		/// </summary>
		void DeletedItem(ISerializableItem deletedItem);
		
		/// <summary>
		/// Fired when an item's name is different between serialized and source
		/// </summary>
		void IsNameMatch(ISerializableItem serializedItem, ISerializableItem existingItem);

		/// <summary>
		/// Fired when an item's template is different between serialized and source
		/// </summary>
		void IsTemplateMatch(ISerializableItem serializedItem, ISerializableItem existingItem);

		/// <summary>
		/// Fired when a shared field value is different between serialized and source
		/// </summary>
		/// <remarks>Note that the sourceValue may be null</remarks>
		void IsSharedFieldMatch(ISerializableItem serializedItem, Guid fieldId, string serializedValue, string sourceValue);

		/// <summary>
		/// Fired when a version's field value is different between serialized and source
		/// </summary>
		/// <remarks>Note that the sourceValue may be null</remarks>
		void IsVersionedFieldMatch(ISerializableItem serializedItem, ISerializableVersion version, Guid fieldId, string serializedValue, string sourceValue);

		/// <summary>
		/// Fired when a later version is found in the serialized version of an item
		/// </summary>
		void NewSerializedVersionMatch(ISerializableVersion newSerializedVersion, ISerializableItem serializedItem, ISerializableItem existingItem);

		/// <summary>
		/// Called when you serialize an item that does not yet exist in the provider
		/// </summary>
		void DeserializedNewItem(ISerializableItem serializedItem);

		/// <summary>
		/// Called when you serialize an updated item that already existed in the provider
		/// </summary>
		void SerializedUpdatedItem(ISerializableItem serializedItem);

		/// <summary>
		/// Called when extra versions exist in a source item compared to the serialized version
		/// </summary>
		void OrphanSourceVersion(ISerializableItem existingItem, ISerializableItem serializedItem, ISerializableVersion[] orphanSourceVersions);
	}
}
