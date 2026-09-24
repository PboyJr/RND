using Godot;

namespace RND.Core;

public static class NetExtensions
{
	/// <summary>Peer id of whoever called the current RPC, including the host calling its own.</summary>
	public static int SenderId(this MultiplayerApi multiplayer)
	{
		int id = multiplayer.GetRemoteSenderId();
		return id != 0 ? id : multiplayer.GetUniqueId(); // 0 when the host calls its own RPC
	}
}
