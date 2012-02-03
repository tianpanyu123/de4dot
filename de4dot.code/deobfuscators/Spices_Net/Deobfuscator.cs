﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Spices_Net {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Spices.Net";
		public const string THE_TYPE = "sn";
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption removeNamespaces;

		public DeobfuscatorInfo()
			: base() {
			inlineMethods = new BoolOption(null, makeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, makeArgName("remove-inlined"), "Remove inlined methods", true);
			removeNamespaces = new BoolOption(null, makeArgName("ns1"), "Clear namespace if there's only one class in it", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				InlineMethods = inlineMethods.get(),
				RemoveInlinedMethods = removeInlinedMethods.get(),
				RemoveNamespaces = removeNamespaces.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				removeNamespaces,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		bool foundSpicesAttribute = false;
		bool startedDeobfuscating = false;

		StringDecrypter stringDecrypter;
		SpicesMethodCallInliner methodCallInliner;

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool RemoveNamespaces { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public override IMethodCallInliner MethodCallInliner {
			get {
				if (CanInlineMethods)
					return methodCallInliner;
				return new NoMethodInliner();
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;

			if (options.RemoveNamespaces)
				this.RenamingOptions |= RenamingOptions.RemoveNamespaceIfOneType;
			else
				this.RenamingOptions &= ~RenamingOptions.RemoveNamespaceIfOneType;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(stringDecrypter.Detected) +
					toInt32(foundSpicesAttribute);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			methodCallInliner = new SpicesMethodCallInliner(module);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			findSpicesAttributes();
		}

		void findSpicesAttributes() {
			foreach (var type in module.Types) {
				if (type.FullName == "NineRays.Decompiler.NotDecompile") {
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					foundSpicesAttribute = true;
					break;
				}
			}
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			methodCallInliner.initialize();

			stringDecrypter.initialize();
			foreach (var info in stringDecrypter.DecrypterInfos) {
				staticStringInliner.add(info.method, (method2, args) => {
					return stringDecrypter.decrypt(method2);
				});
			}
			DeobfuscatedFile.stringDecryptersAdded();

			startedDeobfuscating = true;
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			methodCallInliner.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeInlinedMethods();

			if (Operations.DecryptStrings != OpDecryptString.None) {
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
				addTypeToBeRemoved(stringDecrypter.EncryptedStringsType, "Encrypted strings field type");
				stringDecrypter.cleanUp();
			}

			base.deobfuscateEnd();
		}

		void removeInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;

			var unusedMethods = new UnusedMethodsFinder(module, methodCallInliner.getInlinedMethods(), getRemovedMethods()).find();
			var removedTypes = methodCallInliner.getInlinedTypes(unusedMethods);

			addTypesToBeRemoved(removedTypes.getKeys(), "Obfuscator methods type");
			foreach (var method in unusedMethods) {
				if (!removedTypes.find(method.DeclaringType))
					addMethodToBeRemoved(method, "Inlined method");
			}
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			foreach (var info in stringDecrypter.DecrypterInfos)
				list.Add(info.method.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}