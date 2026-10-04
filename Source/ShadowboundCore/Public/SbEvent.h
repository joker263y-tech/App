#pragma once

#include <utility>
#include <vector>

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// TEvent
//
// The engine-free replacement for C# events (`event Action<...>`), which the
// original core used so the presentation layer could react to damage, death and
// status effects without polling.
//
// It is deliberately not an Unreal delegate: this header must never include an
// engine header (see Tools/check-core-purity.sh), and Unreal's dynamic
// delegates would drag UObject machinery into a module that must stay
// testable with a plain C++ compiler.
//
// Bind returns a handle so a listener can unsubscribe exactly its own callback
// - the C# `-=` semantics, without reference-identity gymnastics.
// -----------------------------------------------------------------------------
template <typename... Args>
class TEvent
{
public:
	using Handler = void (*)(void*, Args...);

	struct FBinding
	{
		int Handle;
		void* Context;
		Handler Fn;
	};

	/// Subscribes a function with a context pointer. Returns a handle for Unbind.
	int Bind(void* context, Handler fn)
	{
		const int handle = ++_nextHandle;
		_bindings.push_back(FBinding{ handle, context, fn });
		return handle;
	}

	/// Removes the subscription identified by handle. Returns true if found.
	bool Unbind(int handle)
	{
		for (std::size_t i = 0; i < _bindings.size(); ++i)
		{
			if (_bindings[i].Handle == handle)
			{
				_bindings.erase(_bindings.begin() + static_cast<std::ptrdiff_t>(i));
				return true;
			}
		}

		return false;
	}

	void Clear() { _bindings.clear(); }

	int Count() const { return static_cast<int>(_bindings.size()); }

	/// Notifies subscribers. Iterates a snapshot: a handler is free to bind or
	/// unbind during notification (a death handler removing itself is normal),
	/// which would otherwise invalidate the walk.
	void Broadcast(Args... args) const
	{
		if (_bindings.empty())
		{
			return;
		}

		const std::vector<FBinding> snapshot = _bindings;
		for (const FBinding& binding : snapshot)
		{
			binding.Fn(binding.Context, args...);
		}
	}

private:
	mutable std::vector<FBinding> _bindings;
	mutable int _nextHandle = 0;
};

} // namespace ShadowboundCore
