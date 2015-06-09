using Sitecore.Data;
using Sitecore.Data.Events;
using Sitecore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using Unicorn.Data;
using Unicorn.Predicates;
using Unicorn.Evaluators;
using System.Diagnostics;
using Rainbow.Model;
using Rainbow.Storage;

namespace Unicorn.Loader
{
	/// <summary>
	/// The loader is the heart of Unicorn syncing. It encapsulates the logic required to walk the tree according to a predicate and invoke the evaluator to decide what to do with the tree items.
	/// </summary>
	public class SerializationLoader
	{
		private int _itemsProcessed;
		protected readonly IDataStore SerializationStore;
		protected readonly IPredicate Predicate;
		protected readonly IEvaluator Evaluator;
		protected readonly ISourceDataStore SourceDataProvider;
		protected readonly ISerializationLoaderLogger Logger;
		protected readonly PredicateRootPathResolver PredicateRootPathResolver;

		public SerializationLoader(IDataStore serializationStore, ISourceDataStore sourceDataProvider, IPredicate predicate, IEvaluator evaluator, ISerializationLoaderLogger logger, PredicateRootPathResolver predicateRootPathResolver)
		{
			Assert.ArgumentNotNull(serializationStore, "serializationProvider");
			Assert.ArgumentNotNull(sourceDataProvider, "sourceDataProvider");
			Assert.ArgumentNotNull(predicate, "predicate");
			Assert.ArgumentNotNull(evaluator, "evaluator");
			Assert.ArgumentNotNull(logger, "logger");
			Assert.ArgumentNotNull(predicateRootPathResolver, "predicateRootPathResolver");

			Logger = logger;
			PredicateRootPathResolver = predicateRootPathResolver;
			Evaluator = evaluator;
			Predicate = predicate;
			SerializationStore = serializationStore;
			SourceDataProvider = sourceDataProvider;
		}

		/// <summary>
		/// Loads all items in the configured predicate
		/// </summary>
		public virtual void LoadAll(IDeserializeFailureRetryer retryer, IConsistencyChecker consistencyChecker)
		{
			Assert.ArgumentNotNull(retryer, "retryer");

			var roots = PredicateRootPathResolver.GetRootSerializedItems();
			LoadAll(roots, retryer, consistencyChecker);
		}

		public virtual void LoadAll(ISerializableItem[] rootItems, IDeserializeFailureRetryer retryer, IConsistencyChecker consistencyChecker, Action<ISerializableItem> rootLoadedCallback = null)
		{
			Assert.ArgumentNotNull(rootItems, "rootItems");
			Assert.IsTrue(rootItems.Length > 0, "No root items were passed!");

			using (new EventDisabler())
			{
				foreach (var rootItem in rootItems)
				{
					LoadTree(rootItem, retryer, consistencyChecker);
					if (rootLoadedCallback != null) rootLoadedCallback(rootItem);
				}
			}
			
			retryer.RetryAll(SourceDataProvider, item => DoLoadItem(item, null), item => LoadTreeRecursive(item, retryer, null));

			SourceDataProvider.DeserializationComplete(rootItems[0].DatabaseName);
		}

		/// <summary>
		/// Loads a tree from serialized items on disk.
		/// </summary>
		protected internal virtual void LoadTree(ISerializableItem rootItem, IDeserializeFailureRetryer retryer, IConsistencyChecker consistencyChecker)
		{
			Assert.ArgumentNotNull(rootItem, "rootItem");
			Assert.ArgumentNotNull(retryer, "retryer");
			Assert.ArgumentNotNull(consistencyChecker, "consistencyChecker");

			_itemsProcessed = 0;
			var timer = new Stopwatch();
			timer.Start();

			Logger.BeginLoadingTree(rootItem);


			// load the root item (LoadTreeRecursive only evaluates children)
			DoLoadItem(rootItem, consistencyChecker);

			// load children of the root
			LoadTreeRecursive(rootItem, retryer, consistencyChecker);

			Logger.EndLoadingTree(rootItem, _itemsProcessed, timer.ElapsedMilliseconds);

			timer.Stop();
		}

		/// <summary>
		/// Recursive method that loads a given tree and retries failures already present if any
		/// </summary>
		protected virtual void LoadTreeRecursive(ISerializableItem root, IDeserializeFailureRetryer retryer, IConsistencyChecker consistencyChecker)
		{
			Assert.ArgumentNotNull(root, "root");
			Assert.ArgumentNotNull(retryer, "retryer");

			var included = Predicate.Includes(root);
			if (!included.IsIncluded)
			{
				Logger.SkippedItemPresentInSerializationProvider(root, Predicate.GetType().Name, SerializationStore.GetType().Name, included.Justification ?? string.Empty);
				return;
			}

			try
			{
				// load the current level
				LoadOneLevel(root, retryer, consistencyChecker);

				// check if we have child paths to recurse down
				var children = SerializationStore.GetChildren(root.Id, root.DatabaseName).ToArray();

				if (children.Length > 0)
				{
					// make sure if a "templates" item exists in the current set, it goes first
					if (children.Length > 1)
					{
						int templateIndex = Array.FindIndex(children, x => x.Path.EndsWith("templates", StringComparison.OrdinalIgnoreCase));

						if (templateIndex > 0)
						{
							var zero = children[0];
							children[0] = children[templateIndex];
							children[templateIndex] = zero;
						}
					}

					// load each child path recursively
					foreach (var child in children)
					{
						LoadTreeRecursive(child, retryer, consistencyChecker);
					}

					// pull out any standard values failures for immediate retrying
					retryer.RetryStandardValuesFailures(item => DoLoadItem(item, null));
				} // children.length > 0
			}
			catch (ConsistencyException)
			{
				throw;
			}
			catch (Exception ex)
			{
				retryer.AddTreeRetry(root, ex);
			}
		}

		/// <summary>
		/// Loads a set of children from a serialized path
		/// </summary>
		protected virtual void LoadOneLevel(ISerializableItem rootSerializedItem, IDeserializeFailureRetryer retryer, IConsistencyChecker consistencyChecker)
		{
			Assert.ArgumentNotNull(rootSerializedItem, "root");
			Assert.ArgumentNotNull(retryer, "retryer");

			var orphanCandidates = new Dictionary<Guid, ISerializableItem>();

			// get the corresponding item from Sitecore
			ISerializableItem rootSourceItem = SourceDataProvider.GetById(rootSerializedItem.DatabaseName, rootSerializedItem.Id);

			// we add all of the root item's direct children to the "maybe orphan" list (we'll remove them as we find matching serialized children)
			if (rootSourceItem != null)
			{
				var rootSourceChildren = SourceDataProvider.GetChildren(rootSourceItem);
				foreach (ISerializableItem child in rootSourceChildren)
				{
					// if the preset includes the child add it to the orphan-candidate list (if we don't deserialize it below, it will be marked orphan)
					var included = Predicate.Includes(child);
					if (included.IsIncluded)
						orphanCandidates[child.Id] = child;
					else
					{
						Logger.SkippedItem(child, Predicate.GetType().Name, included.Justification ?? string.Empty);
					}
				}
			}

			// check for direct children of the target path
			var serializedChildren = SerializationStore.GetChildren(rootSerializedItem.Id, rootSerializedItem.DatabaseName);
			foreach (var serializedChild in serializedChildren)
			{
				try
				{
					if (serializedChild.IsStandardValuesItem())
					{
						orphanCandidates.Remove(serializedChild.Id); // avoid marking standard values items orphans
						retryer.AddItemRetry(serializedChild, new StandardValuesException(serializedChild.Path));
					}
					else
					{
						// load a child item
						var loadedSourceItem = DoLoadItem(serializedChild, consistencyChecker);
						if (loadedSourceItem.Item != null)
						{
							orphanCandidates.Remove(loadedSourceItem.Item.Id);

							// check if we have any child serialized items under this loaded child item (existing children) -
							// if we do not, we can orphan any included children of the loaded item as well
							var loadedItemSerializedChildren = SerializationStore.GetChildren(serializedChild.Id, serializedChild.DatabaseName);

							if (!loadedItemSerializedChildren.Any()) // no children were serialized on disk
							{
								var loadedSourceChildren = SourceDataProvider.GetChildren(loadedSourceItem.Item);
								foreach (ISerializableItem loadedSourceChild in loadedSourceChildren)
								{
									// place any included source children on the orphan list for deletion, as no serialized children existed
									if(Predicate.Includes(loadedSourceChild).IsIncluded)
										orphanCandidates.Add(loadedSourceChild.Id, loadedSourceChild);
								}
							}
						}
						else if (loadedSourceItem.Status == ItemLoadStatus.Skipped) // if the item got skipped we'll prevent it from being deleted
							orphanCandidates.Remove(serializedChild.Id);
					}
				}
				catch (ConsistencyException)
				{
					throw;
				}
				catch (Exception ex)
				{
					// if a problem occurs we attempt to retry later
					retryer.AddItemRetry(serializedChild, ex);

					// don't treat errors as cause to delete an item
					orphanCandidates.Remove(serializedChild.Id);
				}
			}

			// if we're forcing an update (ie deleting stuff not on disk) we send the items that we found that weren't on disk off to get evaluated as orphans
			if (orphanCandidates.Count > 0)
			{
				bool disableNewSerialization = UnicornDataProvider.DisableSerialization;
				try
				{
					UnicornDataProvider.DisableSerialization = true;
					Evaluator.EvaluateOrphans(orphanCandidates.Values.ToArray());
				}
				finally
				{
					UnicornDataProvider.DisableSerialization = disableNewSerialization;
				}
			}
		}

		/// <summary>
		/// Loads a specific item from disk
		/// </summary>
		protected virtual ItemLoadResult DoLoadItem(ISerializableItem serializedItem, IConsistencyChecker consistencyChecker)
		{
			Assert.ArgumentNotNull(serializedItem, "serializedItem");

			if (consistencyChecker != null)
			{
				if (!consistencyChecker.IsConsistent(serializedItem)) throw new ConsistencyException("Consistency check failed - aborting loading.");
				consistencyChecker.AddProcessedItem(serializedItem);
			}

			bool disableNewSerialization = UnicornDataProvider.DisableSerialization;
			try
			{
				UnicornDataProvider.DisableSerialization = true;

				_itemsProcessed++;

				var included = Predicate.Includes(serializedItem);

				if (!included.IsIncluded)
				{
					Logger.SkippedItemPresentInSerializationProvider(serializedItem, Predicate.GetType().Name, SerializationStore.GetType().Name, included.Justification ?? string.Empty);
					return new ItemLoadResult(ItemLoadStatus.Skipped);
				}

				// detect if we should run an update for the item or if it's already up to date
				var existingItem = SourceDataProvider.GetById(serializedItem.DatabaseName, serializedItem.Id);
				ISerializableItem updatedItem;

				// note that the evaluator is responsible for actual action being taken here
				// as well as logging what it does
				if (existingItem == null)
					updatedItem = Evaluator.EvaluateNewSerializedItem(serializedItem);
				else
					updatedItem = Evaluator.EvaluateUpdate(serializedItem, existingItem);

				return new ItemLoadResult(ItemLoadStatus.Success, updatedItem ?? existingItem);
			}
			finally
			{
				UnicornDataProvider.DisableSerialization = disableNewSerialization;
			}
		}

		protected class ItemLoadResult
		{
			public ItemLoadResult(ItemLoadStatus status)
			{
				Item = null;
				Status = status;
			}

			public ItemLoadResult(ItemLoadStatus status, ISerializableItem item)
			{
				Item = item;
				Status = status;
			}

			public ISerializableItem Item { get; private set; }
			public ItemLoadStatus Status { get; private set; }
		}

		/// <summary>
		/// The result from loading a single item from disk
		/// </summary>
		protected enum ItemLoadStatus { Success, Skipped }

	}
}
